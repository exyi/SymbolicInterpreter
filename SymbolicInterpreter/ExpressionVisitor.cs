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
            return base.VisitExtension(node);
        }

        protected virtual Expression VisitMyParameter(MyParameterExpression parameter)
        {
            return parameter;
        }
    }
}
