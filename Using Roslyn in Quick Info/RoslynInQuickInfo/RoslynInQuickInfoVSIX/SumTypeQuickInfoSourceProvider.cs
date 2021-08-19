using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using RoslynInQuickInfo;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoslynInQuickInfoVSIX
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("QuickInfo Provider for Sum Types")]
    [Order(After = "default")]
    [ContentType("CSharp")]
    public class SumTypeQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return new SumTypeQuickInfoSource(textBuffer);
        }
    }
}
