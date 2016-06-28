﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Common.Core;
using Microsoft.R.Core.AST.Operators;
using Microsoft.R.Core.AST.Operators.Definitions;
using Microsoft.R.Core.AST.Scopes;
using Microsoft.R.Core.AST.Statements.Definitions;
using Microsoft.R.Core.AST.Variables;
using Microsoft.R.Core.Parser;

namespace Microsoft.R.Components.Application.Configuration.Parser {
    internal sealed class ConfigurationParser {
        private readonly List<ConfigurationError> _errors = new List<ConfigurationError>();
        private readonly StreamReader _sr;
        private string _bufferedLine;
        private int _lineNumber;

        public IReadOnlyList<ConfigurationError> Errors => _errors;

        public ConfigurationParser(StreamReader sr) {
            _sr = sr;
        }

        public IConfigurationSetting ReadSetting() {
            var setting = new ConfigurationSetting();
            while (!IsEndOfStream()) {
                var line = ReadLine();
                if (!string.IsNullOrWhiteSpace(line)) {
                    if (line.TrimStart()[0] == '#') {
                        ReadAttributeValue(line.Substring(1), setting);
                    } else {
                        // Expression can be multiline so read all lines up to the next comment
                        int startingLineNumber = _lineNumber;
                        var text = line + ReadRemainingExpressionText();
                        if (ParseSetting(text, startingLineNumber, setting)) {
                            return setting;
                        }
                        break;
                    }
                }
            }
            return null;
        }

        private bool IsEndOfStream() {
            return _bufferedLine == null && _sr.EndOfStream;
        }
        private string ReadLine() {
            var line = _bufferedLine ?? _sr.ReadLine();
            _lineNumber++;
            _bufferedLine = null;
            return line;
        }

        private void UnreadLine(string line) {
            Debug.Assert(_bufferedLine == null);
            _bufferedLine = line;
            _lineNumber--;
        }

        private string ReadRemainingExpressionText() {
            var sb = new StringBuilder();
            while (!IsEndOfStream()) {
                var line = ReadLine();
                if (string.IsNullOrWhiteSpace(line)) {
                    break;
                } else if (line.Length > 0 && line.TrimStart()[0] == '#') {
                    UnreadLine(line);
                    break;
                }
                sb.Append(Environment.NewLine);
                sb.Append(line);
            }
            return sb.ToString();
        }

        private bool ParseSetting(string text, int lineNumber, ConfigurationSetting s) {
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }
            // Parse the expression
            var ast = RParser.Parse(text);
            if (ast.Errors.Count == 0) {
                // Expected 'Variable <- Expression'
                var scope = ast.Children[0] as GlobalScope;
                if (scope?.Children.Count > 0) {
                    var exp = (scope.Children[0] as IExpressionStatement)?.Expression;
                    if (exp?.Children.Count == 1) {
                        var op = exp.Children[0] as IOperator;
                        if (op != null) {
                            if (op.OperatorType == OperatorType.LeftAssign && op.LeftOperand != null && op.RightOperand != null) {
                                var name = (op.LeftOperand as Variable)?.Name;
                                var value = text.Substring(op.RightOperand.Start, op.RightOperand.Length);
                                var result = !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value);
                                if (result) {
                                    s.Name = name;
                                    s.Value = value.RemoveQuotes();
                                    s.ValueType = value[0] == '\'' || value[0] == '\"' ? ConfigurationSettingValueType.String : ConfigurationSettingValueType.Expression;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            _errors.Add(new ConfigurationError(lineNumber, Resources.ConfigurationError_Syntax));
            return false;
        }

        private bool ReadAttributeValue(string line, IConfigurationSetting s) {
            line = line.TrimStart();
            foreach (var attribute in ConfigurationSettingAttribute.Attributes) {
                if (line.StartsWith(attribute)) {
                    var attName = ConfigurationSettingAttribute.GetName(attribute);
                    if (!string.IsNullOrWhiteSpace(attName)) {
                        if (s.Attributes.ContainsKey(attName)) {
                            _errors.Add(new ConfigurationError(_lineNumber, Resources.ConfigurationError_DuplicateAttribute));
                        } else {
                            s.Attributes[attName] = line.Substring(attribute.Length).Trim();
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
