﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.R.DataInspection;

namespace Microsoft.VisualStudio.R.Package.DataInspect.Viewers {
    [Export(typeof(IObjectDetailsViewerAggregator))]
    internal sealed class ObjectDetailsViewerAggregator : IObjectDetailsViewerAggregator {
        [ImportMany]
        private IEnumerable<Lazy<IObjectDetailsViewer>> Viewers { get; set; }

        [Import]
        private IDataObjectEvaluator Evaluator { get; set; }

        public async Task<IObjectDetailsViewer> GetViewer(string expression) {
            var preliminary = await Evaluator.EvaluateAsync(expression,
                                RValueProperties.Classes | RValueProperties.Dim | RValueProperties.Length,
                                null)
                                as IRValueInfo;
            if (preliminary != null) {
                return GetViewer(preliminary);
            }
            return null;
        }

        public IObjectDetailsViewer GetViewer(IRValueInfo result) {
            Lazy<IObjectDetailsViewer> lazyViewer = Viewers.FirstOrDefault(x => x.Value.CanView(result));
            return lazyViewer?.Value;
        }
    }
}
