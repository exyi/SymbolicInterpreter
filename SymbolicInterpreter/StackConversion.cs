using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public static class StackConversion
    {
        private static HashSet<Type> integers = new HashSet<Type>
        {
            typeof (sbyte),
            typeof (byte),
            typeof (short),
            typeof (ushort),
            typeof (int),
            typeof (uint),
            typeof (long),
            typeof (ulong),
            typeof (char),
        };

        public static Expression ConvertToSigned(Expression expr, bool throwExc = true)
        {
            var type = expr.Type;
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(sbyte))
                return expr;

            if (type == typeof(uint)) return Expression.Convert(expr, typeof(int));
            if (type == typeof(ushort)) return Expression.Convert(expr, typeof(short));
            if (type == typeof(ulong)) return Expression.Convert(expr, typeof(long));
            if (type == typeof(byte)) return Expression.Convert(expr, typeof(sbyte));
            if (type == typeof(bool)) return Expression.Convert(expr, typeof(int));

            if (throwExc) throw new NotSupportedException();
            else return null;
        }

        public static Expression ConvertToUnsigned(Expression expr, bool throwExc = true)
        {
            var type = expr.Type;
            if (type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(byte))
                return expr;

            if (type == typeof(int)) return Expression.Convert(expr, typeof(uint));
            if (type == typeof(short)) return Expression.Convert(expr, typeof(ushort));
            if (type == typeof(long)) return Expression.Convert(expr, typeof(ulong));
            if (type == typeof(sbyte)) return Expression.Convert(expr, typeof(byte));
            if (type == typeof(bool)) return Expression.Convert(expr, typeof(uint));

            if (throwExc) throw new NotSupportedException();
            else return null;
        }

        public static Expression ConvertToBool(Expression expr, bool throwExc = true)
        {
            var type = expr.Type;
            if (type == typeof(bool)) return expr;
            if (integers.Contains(type)) return Expression.NotEqual(expr, Expression.Default(type));
            if (!type.IsValueType) return Expression.NotEqual(expr, Expression.Constant(null, type));

            if (throwExc) throw new NotSupportedException();
            else return null;
        }

        public static Expression ImplicitConvertTo(Expression from, Type to, bool throwExc = true, bool force = false)
        {
            var type = from.Type;
            if (type == to) return from;
            if (to.IsEnum && from.Type.IsNumericType())
            {
                return Expression.Convert(from, to);
            }
            else if (to == typeof(bool))
            {
                return ConvertToBool(from, throwExc);
            }
            else if (from.Type.IsByRef && from.Type.GetElementType().IsValueType && to == typeof(object))
            {
                if (from is AddressOfExpression)
                    return Expression.Convert(((AddressOfExpression)from).Object, typeof(object));
                else throw new Exception("cant unreference");
            }
            else if (to.IsAssignableFrom(from.Type))
                return Expression.Convert(from, to);

            if (from.NodeType == ExpressionType.Convert && !from.Type.IsValueType)
                return ImplicitConvertTo(((UnaryExpression)from).Operand, to, throwExc);
            else if ((from.IsConstant() && from.GetConstantValue() == null) || (from.NodeType == ExpressionType.Default && !from.Type.IsValueType))
                return Expression.Constant(null, to);

            if (force) return Expression.Convert(from, to);
            else if (throwExc) throw new NotSupportedException();
            else return null;
        }

        public static void OperatorConvert(ref Expression a, ref Expression b)
        {
            if (a.Type == b.Type) return;
            var a2b = ImplicitConvertTo(a, b.Type, false);
            if (a2b != null)
            {
                a = a2b;
                return;
            }
            var b2a = ImplicitConvertTo(b, a.Type, false);
            if (b2a != null)
            {
                b = b2a;
                return;
            }
            throw new NotSupportedException();
        }

		public static Expression UnwrapAddressOf(Expression expression)
		{
			if (expression is AddressOfExpression) return expression.CastTo<AddressOfExpression>().Object;
			return expression;
		}
    }
}
