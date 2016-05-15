using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public static class Utils
    {
        private static readonly HashSet<Type> NumericTypes = new HashSet<Type>()
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
            typeof (float),
            typeof (double),
            typeof (decimal)
        };

        public static bool IsNumericType(this Type type)
        {
            return NumericTypes.Contains(type);
        }

        public static To CastTo<To>(this object o) => (To)o;
        [Obsolete]
        public static BlockExpression TrackParameters(this ExecutionState state)
        {
            var result = new List<Expression>();
            HashSet<Expression> doneMap = new HashSet<Expression>(ExpressionComparer.Instance);

            foreach (var item in state.SetExpressions)
            {
                TrackParametersCore(result, doneMap, state, item.Key, item.Value);
            }

            return Expression.Block(result);
        }
        [Obsolete]
        public static BlockExpression TrackParameters(this ExecutionState state, Expression parameter)
        {
            var assignment = state.TryFindAssignment(parameter);
            var result = new List<Expression>();
            HashSet<Expression> doneMap = new HashSet<Expression>(ExpressionComparer.Instance);
            if (assignment != null)
            {
                TrackParametersCore(result, doneMap, state, parameter, assignment);
            }
            return Expression.Block(result);
        }
        [Obsolete]
        private static void TrackParametersCore(List<Expression> result, HashSet<Expression> doneMap, ExecutionState state, Expression left, Expression right)
        {
            if (doneMap.Contains(left)) return;

            var rqp = right.GetUsedParameters().Concat(left.GetUsedParameters()).Except(new[] { left });

            // resolve deps first
            foreach (var qq in rqp)
            {
                if (doneMap.Contains(qq)) continue;
                var assignment = state.TryFindAssignment(qq);
                if (assignment != null) TrackParametersCore(result, doneMap, state, qq, assignment);
            }

            doneMap.Add(left);
            try { result.Add(Expression.Assign(left, right)); } catch { }

            // write also object modifications
            foreach (var pp in TrackParam(state, left))
            {
                TrackParametersCore(result, doneMap, state, pp.Left, pp.Right);
            }
        }

        /// <summary>
        /// Returns all assignments that contain parameter on the left side.
        /// </summary>
        [Obsolete]
        private static IEnumerable<BinaryExpression> TrackParam(ExecutionState state, Expression parameter)
        {
            foreach (var se in state.SetExpressions)
            {
                if (se.Key.Contains(parameter))
                {
                    BinaryExpression x = null;
                    try
                    {
                        x = Expression.Assign(se.Key, se.Value);
                    }
                    catch { }
                    if (x != null) yield return x;
                }
            }
        }

        public static IEnumerable<ParameterExpression> GetUsedParameters(this Expression expr)
        {
            var v = new ParameterFindingVisitor();
            v.Visit(expr);
            return v.Parameters;
        }

        public static bool Contains(this Expression expr, Expression query)
        {
            var ctv = new ContainsVisitor() { Query = query };
            ctv.Visit(expr);
            return ctv.Result;
        }

        public static string GetDebugView(this Expression expr)
        {
            var prop = typeof(Expression).GetProperty("DebugView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (string)prop.GetValue(PrepareForDebugViewVisitor.Instance.Visit(expr));
        }

        class PrepareForDebugViewVisitor : ExpressionVisitor
        {
            public static readonly PrepareForDebugViewVisitor Instance = new PrepareForDebugViewVisitor();

            protected override Expression VisitMyParameter(MyParameterExpression parameter)
            {
                return Expression.Parameter(parameter.Type, (parameter.IsRoot ? "%" : (parameter.IsMutable ? "" : "!")) + parameter.Name);
            }
        }

        class ContainsVisitor : ExpressionVisitor
        {
            public bool Result { get; set; }
            public Expression Query { get; set; }

            public override Expression Visit(Expression node)
            {
                if (ExpressionComparer.Instance.Equals(node, Query))
                {
                    Result = true;
                }

                if (Result) return node;
                return base.Visit(node);
            }
        }

        public static KeyValuePair<T1, T2> SetKey<T1, T2>(this KeyValuePair<T1, T2> kvp, T1 key)
            => new KeyValuePair<T1, T2>(key, kvp.Value);

        public static KeyValuePair<T1, T2> SetValue<T1, T2>(this KeyValuePair<T1, T2> kvp, T2 value)
            => new KeyValuePair<T1, T2>(kvp.Key, value);


        class ParameterFindingVisitor : ExpressionVisitor
        {
            public HashSet<ParameterExpression> Parameters { get; } = new HashSet<ParameterExpression>(ExpressionComparer.Instance);

            protected override Expression VisitParameter(ParameterExpression node)
            {
                Parameters.Add(node);
                return base.VisitParameter(node);
            }
        }
    }
}
