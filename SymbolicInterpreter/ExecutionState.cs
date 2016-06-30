using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    [Pure]
    public class ExecutionState
    {
        private static ImmutableArray<Expression> Emptystack = ImmutableArray<Expression>.Empty;

        public readonly ImmutableDictionary<Expression, Expression> SetExpressions;
        public readonly ImmutableList<Expression> Conditions;
        public readonly ImmutableArray<Expression> Stack;
        public readonly ExecutionState Parent;
        public readonly ImmutableList<KeyValuePair<Expression, Expression>> SideEffects;

        public ExecutionState(ImmutableDictionary<Expression, Expression> setExpressions = null,
            ImmutableList<Expression> conditions = null,
            ImmutableArray<Expression>? stack = null,
            ExecutionState parent = null,
            ImmutableList<KeyValuePair<Expression, Expression>> sideEffects = null)
        {
            this.SetExpressions = setExpressions ?? ImmutableDictionary<Expression, Expression>.Empty.WithComparers(ExpressionComparer.Instance);
            this.Conditions = conditions ?? ImmutableList<Expression>.Empty;
            this.Stack = stack ?? Emptystack;
            this.Parent = parent;
            this.SideEffects = sideEffects ?? ImmutableList<KeyValuePair<Expression, Expression>>.Empty;
        }

        public ExecutionState()
        {
            this.SetExpressions = ImmutableDictionary<Expression, Expression>.Empty.WithComparers(ExpressionComparer.Instance);
            this.Conditions = ImmutableList<Expression>.Empty;
            this.Stack = Emptystack;
        }

        [Pure]
        public IEnumerable<Expression> GetConditions() => Conditions.Concat(Parent?.Conditions ?? Enumerable.Empty<Expression>());

        [Pure]
        public IEnumerable<KeyValuePair<Expression, Expression>> GetSetExpressions()
            => Parent == null ? SetExpressions : SetExpressions.Concat(Parent.SetExpressions);

        // modifiers
        [Pure]
        public ExecutionState Nest(params Expression[] conditions)
            => new ExecutionState(conditions: conditions.ToImmutableList(), parent: this, stack: Stack);
        [Pure]
        public ExecutionState Nest(IEnumerable<Expression> conditions)
            => new ExecutionState(conditions: conditions.ToImmutableList(), parent: this, stack: Stack);
        [Pure]
        public ExecutionState AddCondition(Expression expr)
        {
            return new ExecutionState(SetExpressions, Conditions.Add(expr), Stack, Parent, SideEffects);
        }
        [Pure]
        public ExecutionState WithSet(Expression prop, Expression value)
        {
            Contract.Requires(prop.Type == value.Type);
            Debug.Assert(prop.Type == value.Type);
            return new ExecutionState(SetExpressions.SetItem(prop, value), Conditions, Stack, Parent, SideEffects);
        }
        [Pure]
        public ExecutionState WithSets(IEnumerable<KeyValuePair<Expression, Expression>> assignments)
        {
            Debug.Assert(assignments.All(a => a.Key.Type == a.Value.Type));
            return new ExecutionState(SetExpressions.SetItems(assignments), Conditions, Stack, Parent, SideEffects);
        }
        [Pure]
        public ExecutionState WithStack(IEnumerable<Expression> newstack)
            => new ExecutionState(SetExpressions, Conditions, newstack.ToImmutableArray(), Parent, SideEffects);
        [Pure]
        public ExecutionState ReplaceTopStack(int popCount, IEnumerable<Expression> newstack)
        {
            if (newstack == null) newstack = Enumerable.Empty<Expression>();
            if (Stack.Length == popCount) return WithStack(newstack);
            if (popCount == Stack.Length) return WithStack(newstack);
            return WithStack(Stack.Take(Stack.Length - popCount).Concat(newstack));
        }
        [Pure]
        public ExecutionState WithStack(Stack<Expression> newstack)
            => WithStack(newstack.Reverse());
        [Pure]
        public ExecutionState ClearStack()
            => new ExecutionState(SetExpressions, Conditions, ImmutableArray.Create<Expression>(), Parent, SideEffects);

        public bool HasStack() => Stack.Length > 0;

        [Pure]
        public ExecutionState WithException(Expression exception)
            => AddSideEffect(Expression.Throw(exception.Resolve(this)));
        [Pure]
        public ExecutionState AddSideEffect(Expression sideEffect, Expression condition = null)
            => new ExecutionState(SetExpressions, Conditions, Stack, Parent, SideEffects.Add(new KeyValuePair<Expression, Expression>(condition, sideEffect)));

        [Pure]
        public ExecutionState AddSideEffects(IEnumerable<KeyValuePair<Expression, Expression>> sd)
           => new ExecutionState(SetExpressions, Conditions, Stack, Parent, SideEffects.AddRange(sd));
        [Pure]
        public ExecutionState WithException(Expression<Action<Exception>> exception)
            => WithException(exception.Body);

        // read helpers
        [Pure]
        public Expression TryFindAssignment(Expression left)
        {
            Expression result;
            SetExpressions.TryGetValue(left, out result);
            return result ?? Parent?.TryFindAssignment(left);
        }

        public override string ToString()
        {
            var sb = new StringBuilder("Execution State");

            sb.Append("[");
            sb.Append(string.Join(", ", Stack));
            sb.AppendLine("]");

            sb.Append("\n\nSetExpression:\n================\n\n");
            sb.Append(this.TrackParameters().GetDebugView());

            sb.Append("\n\n-------------------------------------------------------------\nSideEffects:\n\n");
            foreach (var se in SideEffects)
            {
                var expr = se.Value;
                if (se.Key != null) expr = Expression.IfThen(se.Key, se.Value);
                sb.AppendLine(expr.GetDebugView());
            }

            return sb.ToString();
        }
    }
}
