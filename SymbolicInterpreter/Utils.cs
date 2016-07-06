using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public static class Utils
    {
        public static To CastTo<To>(this object o) => (To)o;
        public static To As<To>(this object o) where To: class => o as To;
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

        public static KeyValuePair<T1, T2> SetKey<T1, T2>(this KeyValuePair<T1, T2> kvp, T1 key)
            => new KeyValuePair<T1, T2>(key, kvp.Value);

        public static KeyValuePair<T1, T2> SetValue<T1, T2>(this KeyValuePair<T1, T2> kvp, T2 value)
            => new KeyValuePair<T1, T2>(kvp.Key, value);

        public static IEnumerable<FieldInfo> GetAllFields(this Type type, bool instance = true)
        {
            while(type != typeof(object))
            {
                foreach (var f in type.GetFields((instance ? BindingFlags.Instance : BindingFlags.Static) | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    yield return f;
                type = type.BaseType;
            }
        }

        public static TValue FindOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key)
        {
            TValue result;
            dict.TryGetValue(key, out result);
            return result;
        }
    }
}
