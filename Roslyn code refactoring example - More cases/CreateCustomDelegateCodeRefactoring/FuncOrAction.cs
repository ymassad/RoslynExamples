using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace CreateCustomDelegateCodeRefactoring
{
    public abstract class FuncOrAction
    {
        private FuncOrAction() { }
        public sealed class Func : FuncOrAction
        {
            public ImmutableArray<Parameter> Parameters { get; }
            public ITypeSymbol ReturnType { get; }
            public Func(ImmutableArray<Parameter> parameters, ITypeSymbol returnType)
            {
                Parameters = parameters;
                ReturnType = returnType;
            }
        }

        public sealed class Action : FuncOrAction
        {
            public ImmutableArray<Parameter> Parameters { get; }
            public Action(ImmutableArray<Parameter> parameters)
            {
                Parameters = parameters;
            }
        }
    }

}
