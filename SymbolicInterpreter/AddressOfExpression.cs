using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public class AddressOfExpression: Expression
    {
        public override ExpressionType NodeType => ExpressionType.Extension;
        public Expression Object { get; }
        public override Type Type => Object.Type.MakeByRefType();
        public override string ToString() => $"&({Object})";
        public override bool Equals(object obj)
        {
            return obj is AddressOfExpression && ExpressionComparer.Instance.Equals(Object, ((AddressOfExpression)obj).Object);
        }
        public override int GetHashCode() => 
            ExpressionComparer.Instance.GetHashCode(Object) * 47 + 148526;

        public AddressOfExpression(Expression obj)
        {
            Object = obj;
        }

        public AddressOfExpression Update(Expression obj)
        {
            if (obj == Object) return this;
            else return new AddressOfExpression(obj);
        }
    }
}
