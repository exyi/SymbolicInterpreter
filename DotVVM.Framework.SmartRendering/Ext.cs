using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DotVVM.Framework.SmartRendering
{
    public static class Ext
    {
        public static Expression CallToString(this Expression expression)
        {
            if (expression.Type == typeof(string)) return expression;
            return Expression.Call(expression, expression.Type.GetMethod("ToString", Type.EmptyTypes));
        }
    }
}
