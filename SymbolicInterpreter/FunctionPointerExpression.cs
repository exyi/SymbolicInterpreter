using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public class FunctionPointerExpression: Expression
    {
        public override ExpressionType NodeType => ExpressionType.Extension;
        public MethodInfo Method { get; }
        public override Type Type => typeof(IntPtr);
        public override string ToString() => $"&({Method})";
        public override bool Equals(object obj)
        {
            return obj is FunctionPointerExpression && ExpressionComparer.Equals(Method, ((FunctionPointerExpression)obj).Method);
        }
        public override int GetHashCode() =>
            ExpressionComparer.GetHashCode(Method) * 47 + 148526;

        public FunctionPointerExpression(MethodInfo method)
        {
            this.Method = method;
        }
    }
}
