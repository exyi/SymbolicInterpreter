using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public class ExpressionVisitor: System.Linq.Expressions.ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is MyParameterExpression) return VisitMyParameter((MyParameterExpression)node);
            else if (node is AddressOfExpression) return VisitAddressOf((AddressOfExpression)node);
            else if (node is FunctionPointerExpression) return VisitFunctionPointer(node.CastTo<FunctionPointerExpression>());
            return base.VisitExtension(node);
        }

        protected virtual Expression VisitFunctionPointer(FunctionPointerExpression ftn)
        {
            return ftn;
        }

        protected virtual Expression VisitMyParameter(MyParameterExpression parameter)
        {
            return parameter;
        }

        protected virtual Expression VisitAddressOf(AddressOfExpression addrof)
        {
            return addrof.Update(Visit(addrof.Object));
        }
    }
}
