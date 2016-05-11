using DotVVM.Framework.Compilation.ControlTree.Resolved;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using SymbolicInterpreter;

namespace DotVVM.Framework.SmartRendering
{
    public class ControlAnalyzer
    {
        public SymbolicExecutor Executor { get; set; }

        public ControlAnalyzer(SymbolicExecutor executor)
        {
            Executor = executor;
        }

        public void Analyze(ResolvedControl control)
        {

        }

        public ExecutionState CreateExecutionState(ResolvedControl control, Expression controlParameter)
        {
            var conditions = new List<Expression>();
            var assignments = new List<BinaryExpression>();
            var controlCtor = control.Metadata.Type.GetConstructors().Single(c => c.GetParameters().Length == control.ConstructorParameters.Length);
            //assignments.Add(Expression.Assign(controlParameter, 
            //    Expression.New(
            //        control.Metadata.Type.GetConstructors().Single(c => c.GetParameters().Length == control.ConstructorParameters.Length),
            //        control.ConstructorParameters.Select(Expression.Constant))));
            var mc = new MethodContext();

            var state = new ExecutionState(setExpressions: assignments.ToImmutableDictionary(k => k.Left, k => k.Right, ExpressionComparer.Instance), conditions: conditions.ToImmutableList());

            state = Executor.CallCtor(controlCtor, state.WithStack(control.ConstructorParameters.Select(Expression.Constant)));
            var thisParam = state.Stack.Single();
            state.WithSet(controlParameter, StackConversion.ImplicitConvertTo(thisParam, controlParameter.Type));

            return state;
        }
    }
}
