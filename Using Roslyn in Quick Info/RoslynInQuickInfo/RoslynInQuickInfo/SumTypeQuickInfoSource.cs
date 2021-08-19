using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynInQuickInfo
{
    public class SumTypeQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer textBuffer;

        public SumTypeQuickInfoSource(ITextBuffer textBuffer)
        {
            this.textBuffer = textBuffer;
        }

        public void Dispose()
        {
            
        }

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(
            IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            var snapshot = textBuffer.CurrentSnapshot;

            var triggerPoint = session.GetTriggerPoint(snapshot);

            if (triggerPoint is null)
                return null;

            var position = triggerPoint.Value.Position;

            var document = snapshot.GetOpenDocumentInCurrentContextWithChanges();

            var result = await CalculateQuickInfo(document, position, cancellationToken);

            if (!result.HasValue)
                return null;

            var (message, span) = result.Value;

            return new QuickInfoItem(
                snapshot.CreateTrackingSpan(new Span(span.Start, span.Length), SpanTrackingMode.EdgeExclusive),
                message);
        }

        public static async Task<(string message, TextSpan span)?> CalculateQuickInfo(
            Document document,
            int position,
            CancellationToken cancellationToken)
        {
            var rootNode = await document.GetSyntaxRootAsync();

            var node = rootNode.FindNode(TextSpan.FromBounds(position, position));

            if(!(node is IdentifierNameSyntax identifierNameSyntax))
            {
                return null;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (!(semanticModel.GetSymbolInfo(identifierNameSyntax).Symbol is INamedTypeSymbol symbol))
                return null;

            if (symbol.TypeKind != TypeKind.Class)
                return null;

            if (!symbol.IsAbstract)
                return null;

            if (symbol.Constructors.Length != 1)
                return null;

            if (symbol.Constructors[0].DeclaredAccessibility != Accessibility.Private)
                return null;

            var subclasses = symbol.GetMembers()
                .OfType<INamedTypeSymbol>()
                .Where(x => x.TypeKind == TypeKind.Class)
                .Where(x => x.BaseType?.Equals(symbol, SymbolEqualityComparer.Default) ?? false)
                .Where(x => !x.IsGenericType)
                .ToArray();

            if (subclasses.Length == 0)
                return null;

            var message = "Sum type cases:" + Environment.NewLine
                + string.Join(Environment.NewLine, subclasses.Select(x => x.Name));

            return (message, identifierNameSyntax.Span);
        }
    }
}
