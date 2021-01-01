using Microsoft.CodeAnalysis;

namespace CreateCustomDelegateCodeRefactoring
{
    public class Parameter
    {
        public Parameter(string name, ITypeSymbol type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }

        public ITypeSymbol Type { get; }
    }

}
