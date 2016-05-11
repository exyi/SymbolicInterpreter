using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public static class ExpressionSolver
    {
        public static Type ProveType(this Expression expr, ExecutionState state = null)
        {
            while (expr.NodeType == ExpressionType.Convert)
            {
                expr = ((UnaryExpression)expr).Operand;
            }


            var expr2 = state.TryFindAssignment(expr);
            while (expr2 != null)
            {
                expr = expr2;
                while (expr.NodeType == ExpressionType.Convert)
                {
                    expr = ((UnaryExpression)expr).Operand;
                }

                expr2 = state.TryFindAssignment(expr2);
            }

            if (expr.NodeType == ExpressionType.Conditional)
            {
                // try to prove both condition ways
                var condition = (ConditionalExpression)expr;
                var trueTrue = condition.IfTrue.ProveType(state);
                var trueFalse = condition.IfFalse.ProveType(state);
                if (trueFalse == trueTrue) return trueFalse;
                else return null;
            }

            if (expr.NodeType == ExpressionType.New || expr.NodeType == ExpressionType.Constant)
            {
                return expr.Type;
            }
            // watch for conditions like <P>.GetType() == typeof(WHATEVER)
            return null;
        }

        public static Expression Simplify(this Expression expr, ExecutionState state = null, bool trackCondition = true)
        {
            Debug.Assert(expr != null);
            var s = new SimplificationVisitor();
            return s.Visit(expr);
        }

        public static ExecutionState ResolveStack(this ExecutionState state)
        {
            if (state.Stack.Length == 0) return state;
            return state.WithStack(state.Stack.Select(e => e.Resolve(state)));
        }

        public static Expression FindThrowException(this ExecutionState state)
        {
            if (state.HasStack()) return null;
            for (int i = state.SideEffects.Count - 1; i >= 0; i--)
            {
                if (state.SideEffects[i].Key == null && state.SideEffects[i].Value.NodeType == ExpressionType.Throw)
                {
                    return ((UnaryExpression)state.SideEffects[i].Value).Operand;
                }
            }
            return null;
        }

        public static Expression Resolve(this Expression expr, ExecutionState state, bool fullResolve = false, bool paramOnly = false)
        {
            if (fullResolve) expr = expr.Resolve(state);
            var rv = new ResolvingVisitor(state, fullResolve, paramOnly);
            var result = rv.Visit(expr).Simplify();
            Debug.Assert(result != null);
            return result;
        }

        public static IEnumerable<ConditionalExpression> EnumerateBranches(this ConditionalExpression condition)
        {
            if (condition.IfTrue.NodeType == ExpressionType.Conditional)
            {
                foreach (var b in EnumerateBranches((ConditionalExpression)condition.IfTrue))
                {
                    yield return b.Update(Expression.And(condition.Test, b.Test), b.IfTrue, b.IfFalse);
                }
            }
            else
            {
                yield return Expression.Condition(condition.Test, condition.IfTrue, Expression.Default(condition.IfTrue.Type));
            }
            Expression notCondition = condition.Test.NodeType == ExpressionType.Not ? ((UnaryExpression)condition.Test).Operand : Expression.Not(condition.Test);
            if (condition.IfFalse.NodeType == ExpressionType.Conditional)
            {
                foreach (var b in EnumerateBranches((ConditionalExpression)condition.IfFalse))
                {
                    yield return b.Update(Expression.And(notCondition, b.Test), b.IfTrue, b.IfFalse);
                }
            }
            else
            {
                yield return Expression.Condition(notCondition, condition.IfFalse, Expression.Default(condition.IfFalse.Type));
            }
        }

        class ResolvingVisitor : SimplificationVisitor
        {
            public ExecutionState State { get; }
            public bool FullResolve { get; }
            public bool ParamOnly { get; }
            private bool isFirst = true;
            private IsOnlyTrivialVisitor iotv;

            public ResolvingVisitor(ExecutionState state, bool fullResolve = false, bool paramOnly = false)
            {
                State = state;
                ParamOnly = paramOnly;
                FullResolve = fullResolve;
                iotv = new IsOnlyTrivialVisitor();
                if (paramOnly)
                {
                    iotv.AllowConstant = iotv.AllowMemberAccess = iotv.AllowOperators = iotv.AllowMethodCall = iotv.AllowConditions = false;
                }
            }

            public override Expression Visit(Expression node)
            {
                if (node == null) return null;
                var isFirst = this.isFirst;
                this.isFirst = false;
                node = base.Visit(node);
                if (node == null) return null;
                var assignRight = State.TryFindAssignment(node);
                if (assignRight != null && (!isFirst || !ParamOnly))
                {
                    if (FullResolve || iotv.IsTrivial(assignRight) || (node is MyParameterExpression && !((MyParameterExpression)node).IsRoot))
                    {
                        return Visit(assignRight);
                    }
                }
                return node;
            }

            class IsOnlyTrivialVisitor : ExpressionVisitor
            {
                public bool AllowConstant { get; set; } = true;
                public bool AllowOperators { get; set; } = true;
                public bool AllowMemberAccess { get; set; } = true;
                public bool AllowMethodCall { get; set; } = true;
                public bool AllowConditions { get; set; } = true;
                public bool Result { get; set; } = true;

                public bool IsTrivial(Expression expr)
                {
                    Result = true;
                    Visit(expr);
                    return Result;
                }

                public override Expression Visit(Expression node)
                {
                    if (!Result) return node;
                    var r = base.Visit(node);
                    if (r != null) Result = false;
                    return node;
                }

                protected override Expression VisitParameter(ParameterExpression node)
                {
                    return null;
                }

                protected override Expression VisitMyParameter(MyParameterExpression parameter)
                {
                    return null;
                }

                protected override Expression VisitConstant(ConstantExpression node)
                {
                    if (AllowConstant) return null;
                    else return node;
                }
                protected override Expression VisitMember(MemberExpression node)
                {
                    base.VisitMember(node);
                    return AllowMemberAccess ? null : node;
                }
                protected override Expression VisitBinary(BinaryExpression node)
                {
                    base.VisitBinary(node);
                    return AllowOperators ? null : node;
                }
                protected override Expression VisitUnary(UnaryExpression node)
                {
                    base.VisitUnary(node);
                    return AllowOperators ? null : node;
                }
                protected override Expression VisitMethodCall(MethodCallExpression node)
                {
                    base.VisitMethodCall(node);
                    return AllowMethodCall ? null : node;
                }
                protected override Expression VisitConditional(ConditionalExpression node)
                {
                    base.VisitConditional(node);
                    return AllowConditions ? null : node;
                }
            }
        }

        class SimplificationVisitor : ExpressionVisitor
        {
            public override Expression Visit(Expression node)
            {
                var result = base.Visit(node);
                Debug.Assert(result.Type == node.Type);
                return result;
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                // field access
                node = (MemberExpression)base.VisitMember(node);

                // (if (a) { x } else { y }).mem
                // (if (a) { x.mem } else { y.mem })
                if (node.Expression.NodeType == ExpressionType.Conditional)
                {
                    var condition = (ConditionalExpression)node.Expression;
                    return Visit(Expression.Condition(condition.Test, node.Update(condition.IfTrue), node.Update(condition.IfFalse)));
                }
                return node;
            }

            protected override Expression VisitDefault(DefaultExpression expr)
            {
                if (expr.Type.IsValueType)
                    return Expression.Constant(Activator.CreateInstance(expr.Type));
                else return Expression.Constant(null, expr.Type);
            }

            protected override Expression VisitConditional(ConditionalExpression node)
            {
                node = (ConditionalExpression)base.VisitConditional(node);
                if (node.Test.IsConstant())
                {
                    if ((bool)node.Test.GetConstantValue())
                    {
                        return node.IfTrue;
                    }
                    else
                    {
                        return node.IfFalse;
                    }
                }

                if (node.IfTrue.NodeType == ExpressionType.Conditional)
                {
                    if (ExpressionComparer.Instance.Equals(node.Test, ((ConditionalExpression)node.IfTrue).Test))
                    {
                        // if (a) { if(a) { .... } } => if(a) { .... }
                        node = node.Update(node.Test, ((ConditionalExpression)node.IfTrue).IfTrue, node.IfFalse);
                    }
                    else if (ExpressionComparer.Instance.Equals(node.Test, VisitUnary(Expression.Not(((ConditionalExpression)node.IfTrue).Test))))
                    {
                        // if (a) { if(!a) { .... } } => if (a) { else ... }
                        node = node.Update(node.Test, ((ConditionalExpression)node.IfTrue).IfFalse, node.IfFalse);
                    }
                }
                if (node.IfFalse.NodeType == ExpressionType.Conditional)
                {
                    if (ExpressionComparer.Instance.Equals(node.Test, ((ConditionalExpression)node.IfFalse).Test))
                    {
                        node = node.Update(node.Test, node.IfTrue, ((ConditionalExpression)node.IfFalse).IfFalse);
                    }
                    else if (ExpressionComparer.Instance.Equals(node.Test, VisitUnary(Expression.Not(((ConditionalExpression)node.IfFalse).Test))))
                    {
                        node = node.Update(node.Test, node.IfTrue, ((ConditionalExpression)node.IfFalse).IfTrue);
                    }
                }

                if (ExpressionComparer.Instance.Equals(node.IfFalse, node.IfTrue)) return node.IfTrue;
                return node;
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                node = (MethodCallExpression)base.VisitMethodCall(node);
                if (node.Object?.IsConstant() != false && node.Arguments.All(s => s.IsConstant()))
                {
                    return Expression.Constant(node.Method.Invoke(node.Object?.GetConstantValue(), node.Arguments.Select(a => a.GetConstantValue()).ToArray()), node.Type);
                }
                return node;
            }
            [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]

            protected override Expression VisitBinary(BinaryExpression node)
            {
                node = (BinaryExpression)base.VisitBinary(node);
                Debug.Assert(node.Method == null);
                switch (node.NodeType)
                {
                    case ExpressionType.Add:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => unchecked(a + b));
                    case ExpressionType.AddChecked:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => checked(a + b));
                    case ExpressionType.And:
                        if (node.Left.IsConstant())
                        {
                            if ((bool)node.Left.GetConstantValue()) return node.Right;
                            else return Expression.Constant(false);
                        }
                        if (node.Right.IsConstant())
                        {
                            if ((bool)node.Right.GetConstantValue()) return node.Left;
                            else return Expression.Constant(false);
                        }
                        return node;
                        break;
                    case ExpressionType.ArrayIndex:
                        return EvalOnValueSource(node, (dynamic array, dynamic index) => array[index]);
                    case ExpressionType.Divide:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => a / b);
                    case ExpressionType.Equal:
                        if (node.Left.Type == typeof(bool))
                        {
                            if (node.Left.IsConstant())
                            {
                                if ((bool)node.Left.GetConstantValue())
                                    return node.Right;
                                else return Expression.Not(node.Right);
                            }
                            else
                            {
                                if ((bool)node.Right.GetConstantValue())
                                    return node.Left;
                                else return Expression.Not(node.Left);
                            }
                        }
                        if (node.Left.NodeType == ExpressionType.Conditional)
                        {
                            return SimplifyConditionEqualsTo((ConditionalExpression)node.Left, node.Right);
                        }
                        if (node.Right.NodeType == ExpressionType.Conditional)
                        {
                            return SimplifyConditionEqualsTo((ConditionalExpression)node.Right, node.Left);
                        }
                        if (node.Right.IsConstant() && !CanEqual(node.Left, node.Right as ConstantExpression)) return Expression.Constant(false);
                        if (node.Left.IsConstant() && !CanEqual(node.Right, node.Left as ConstantExpression)) return Expression.Constant(false);
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => a == b);
                    case ExpressionType.ExclusiveOr:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => a ^ b);
                    case ExpressionType.GreaterThan:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => a > b);
                    case ExpressionType.GreaterThanOrEqual:
                        return Visit(Expression.Not(Expression.LessThan(node.Left, node.Right)));
                    case ExpressionType.LeftShift:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => a << b);
                    case ExpressionType.LessThan:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => a < b);
                    case ExpressionType.LessThanOrEqual:
                        return Visit(Expression.Not(Expression.GreaterThan(node.Left, node.Right)));
                    case ExpressionType.Modulo:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => a % b);
                    case ExpressionType.Multiply:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => unchecked(a * b));
                    case ExpressionType.MultiplyChecked:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => checked(a * b));
                    case ExpressionType.NotEqual:
                        return Visit(Expression.Not(Expression.Equal(node.Left, node.Right)));
                    case ExpressionType.Or:
                        if (node.Left.IsConstant())
                        {
                            if (!(bool)node.Left.GetConstantValue()) return node.Right;
                            else return Expression.Constant(true);
                        }
                        if (node.Right.IsConstant())
                        {
                            if (!(bool)node.Right.GetConstantValue()) return node.Left;
                            else return Expression.Constant(true);
                        }
                        return node;
                        break;
                    case ExpressionType.Power:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => Math.Pow(a, b));
                    case ExpressionType.RightShift:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => a >> b);
                    case ExpressionType.Subtract:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => unchecked(a - b));
                    case ExpressionType.SubtractChecked:
                        return EvalOnValueSource(node, (dynamic a, dynamic b) => checked(a - b));
                    default:
                        throw new NotSupportedException();
                }
                throw new NotImplementedException();
            }

            private static bool CanEqual(Expression expr, ConstantExpression constant)
            {
                var parameter = expr as MyParameterExpression;
                if (parameter != null)
                {
                    if (parameter.NotNull && constant.Value == null) return false;
                    if (parameter.ExactType && constant.Type != parameter.Type) return false;
                }
                else if (expr.NodeType == ExpressionType.Convert && !expr.Type.IsValueType)
                {
                    return CanEqual(((UnaryExpression)expr).Operand, constant);
                }
                return true;
            }

            protected static Expression EvalOnValueSource(BinaryExpression binexpr, Func<object, object, object> constantCallback)
            {
                var left = binexpr.Left;
                var right = binexpr.Right;
                if (left.NodeType == ExpressionType.Constant && right.NodeType == ExpressionType.Constant) return Expression.Constant(constantCallback(left.GetConstantValue(), right.GetConstantValue()));
                // TODO: 
                return binexpr;
            }

            /// <summary>
            /// Reduces 
            /// `(if (a) { pp } else { pg }) == pg`
            /// `!a`
            /// </summary>
            protected Expression SimplifyConditionEqualsTo(ConditionalExpression conditional, Expression equalTo)
            {
                var passingConditions = new List<Expression>();
                foreach (var brach in EnumerateBranches(conditional))
                {
                    var test = brach.Test;
                    var value = brach.IfTrue;

                    var resultCondition = VisitBinary(Expression.And(test, Expression.Equal(value, equalTo)));
                    if (resultCondition.IsConstant())
                    {
                        if ((bool)resultCondition.GetConstantValue())
                        {
                            return Expression.Constant(true);
                        }
                        else;// ignore
                    }
                    else
                    {
                        passingConditions.Add(resultCondition);
                    }
                }
                if (passingConditions.Count == 0) return Expression.Constant(false);
                else if (passingConditions.Count == 1) return passingConditions.Single();
                else
                {
                    var result = Expression.Or(passingConditions[0], passingConditions[1]);
                    for (int i = 2; i < passingConditions.Count; i++)
                    {
                        result = Expression.Or(result, passingConditions[i]);
                    }
                    return result;
                }
            }

            [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
            protected override Expression VisitUnary(UnaryExpression node)
            {
                node = (UnaryExpression)base.VisitUnary(node);
                Debug.Assert(node.Method == null);
                switch (node.NodeType)
                {
                    case ExpressionType.ArrayLength:
                        return EvalOnValueSource<NewArrayExpression>(node, node.Operand,
                            (object constant) => ((Array)constant).Length,
                            e => e.NodeType == ExpressionType.NewArrayBounds && e.Expressions.Count == 1,
                            e => e.Expressions.First()) ?? node;
                    case ExpressionType.Convert:
                    case ExpressionType.ConvertChecked:
                        return EvalOnValueSource<Expression>(node, node.Operand,
                            (object constant) => DynamicCast(constant, node.Type, node.NodeType == ExpressionType.ConvertChecked),
                            e => true,
                            e =>
                            {
                                if (e.NodeType == ExpressionType.Convert && ((UnaryExpression)e).Operand.Type == node.Type) return ((UnaryExpression)e).Operand;
                                if (e.Type == node.Type) return e;
                                return null;
                            }) ?? node;
                    case ExpressionType.Negate:
                        return EvalOnValueSource<UnaryExpression>(node, node.Operand,
                            (dynamic constant) => unchecked(-constant),
                            e => e.NodeType == ExpressionType.Negate,
                            e => e.Operand) ?? node;
                    case ExpressionType.UnaryPlus:
                        return node.Operand;
                    case ExpressionType.NegateChecked:
                        return EvalOnValueSource<UnaryExpression>(node, node.Operand,
                            (dynamic constant) => checked(-constant),
                            e => e.NodeType == ExpressionType.NegateChecked,
                            e => e.Operand) ?? node;
                    case ExpressionType.Not:
                        return EvalOnValueSource<UnaryExpression>(node, node.Operand,
                            (dynamic constant) => !constant,
                            e => e.NodeType == ExpressionType.Not,
                            e => e.Operand) ?? node;
                    case ExpressionType.TypeAs:
                        return EvalOnValueSource<Expression>(node, node.Operand,
                            (object constant) => TypeAsCast(constant, node.Type),
                            e => true,
                            e =>
                            {
                                var pt = e.ProveType();
                                if (pt != null)
                                {
                                    if (node.Type.IsAssignableFrom(pt)) return Expression.Constant(null, e.Type);
                                    else return e;
                                }
                                return Expression.TypeAs(e, node.Type);
                            }) ?? node;
                    // TODO: <whatever> ? <not a type> as T : <any> as T  ==> <whatever> ? null : <any> as T
                    case ExpressionType.Decrement:
                        break;
                    case ExpressionType.Increment:
                        break;
                    case ExpressionType.Unbox:
                        return node.Operand;
                    case ExpressionType.OnesComplement:
                        return EvalOnValueSource<UnaryExpression>(node, node.Operand,
                            (dynamic constant) => ~constant,
                            e => e.NodeType == ExpressionType.Not,
                            e => e.Operand) ?? node;
                    case ExpressionType.IsTrue:
                    case ExpressionType.IsFalse:
                    default:
                        throw new NotImplementedException();
                }
                throw new NotImplementedException();
            }

            private Expression EvalOnValueSource<T>(Expression from, Expression expr, Func<object, object> constantValue, Func<T, bool> predicate, Func<T, Expression> transform)
                where T : Expression
                => EvalOnValueSource(expr, e =>
                {
                    if (e.NodeType == ExpressionType.Constant) return Expression.Constant(constantValue(e.GetConstantValue()), from.Type);
                    if (e is T && predicate((T)e)) return transform((T)e);
                    return null;
                });


            private Expression EvalOnValueSource(Expression expr, Func<Expression, bool> predicate, Func<Expression, Expression> transform)
                => EvalOnValueSource(expr, e => predicate(e) ? transform(e) : null);
            private Expression EvalOnValueSource(Expression expr, Func<Expression, Expression> transform)
            {
                var visitor = new EvalOnValueSourceVisitor(transform);
                var result = visitor.Visit(expr);
                if (visitor.Success) return result;
                else return null;
            }

            private static object TypeAsCast(object val, Type type)
            {
                if (val != null && !type.IsAssignableFrom(val.GetType()))
                    return null;
                else return val;
            }

            private static MethodInfo _genericCastMethod = typeof(SimplificationVisitor).GetMethod("GenericCast", BindingFlags.Static | BindingFlags.NonPublic);
            private static object DynamicCast(object val, Type type, bool isChecked)
            {
                if (!type.IsValueType)
                {
                    if (val != null && !type.IsAssignableFrom(val.GetType()))
                        throw new Exception("Invalid cast");
                    else return val;
                }
                Debug.Assert(val != null);
                return _genericCastMethod.MakeGenericMethod(type).Invoke(null, new[] { val, isChecked });
            }

            private static T GenericCast<T>(dynamic val, bool ch)
            {
                if (ch) return checked((T)val);
                else return unchecked((T)val);
            }
        }

        public class EvalOnValueSourceVisitor
        {
            public Func<Expression, Expression> Transform { get; set; }
            public bool Success { get; set; } = true;

            public EvalOnValueSourceVisitor(Func<Expression, Expression> transform)
            {
                Transform = transform;
            }

            public Expression Visit(Expression node)
            {
                if (!Success) return node;

                var tr = Transform(node);
                if (tr != null) return tr;

                switch (node.NodeType)
                {
                    case ExpressionType.Conditional:
                        var conditional = ((ConditionalExpression)node);
                        return conditional.Update(conditional.Test, Visit(conditional.IfTrue), Visit(conditional.IfFalse));
                    default:
                        Success = false;
                        return node;
                }
            }
        }
    }
}
