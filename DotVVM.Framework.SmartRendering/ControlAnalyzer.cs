using DotVVM.Framework.Compilation.ControlTree.Resolved;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using SymbolicInterpreter;
using DotVVM.Framework.Controls;
using DotVVM.Framework.Hosting;
using System.Reflection;
using DotVVM.Framework.Binding;

namespace DotVVM.Framework.SmartRendering
{
    public class ControlAnalyzer
    {
        public SymbolicExecutor Executor { get; set; }

        public ControlAnalyzer(SymbolicExecutor executor)
        {
            Executor = executor;
        }

        private MethodInfo[] methods = {
            typeof(DotvvmControl).GetMethod("OnPreInit", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(DotvvmControl).GetMethod("OnInit", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(DotvvmControl).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(DotvvmControl).GetMethod("OnPreRender", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(DotvvmControl).GetMethod("OnPreRenderComplete", BindingFlags.Instance | BindingFlags.NonPublic),
        };

        public void Analyze(ResolvedControl control)
        {

        }

        public ExecutionState ExecuteControlLifecycle(ResolvedControl control)
        {
            var state = CreateExecutionState(control);
            var controlParameter = (MyParameterExpression)state.Stack.Single();
            var context = MyExpression.RootParameter(typeof(IDotvvmRequestContext), "dotvvmRequestContext");
            foreach (var m in methods)
            {
                state = CallLifecycleMethod(state, m, controlParameter, context);
            }
            var writer = MyExpression.RootParameter(typeof(IHtmlWriter), "writer");
            state = Executor.CallMethod(typeof(DotvvmControl).GetMethod("Render"), state.WithStack(new[] { controlParameter, writer, context }), true);
            return state;
        }

        private ExecutionState CallLifecycleMethod(ExecutionState state, MethodInfo method, Expression control, Expression context) =>
            Executor.CallMethod(method, state.WithStack(new[] { control, context }), true);

        public ExecutionState CreateExecutionState(ResolvedControl control)
        {
            var conditions = new List<Expression>();
            var assignments = new List<BinaryExpression>();
            var ctorParams = control.ConstructorParameters ?? new object[0];
            var controlCtor = control.Metadata.Type.GetConstructors().Single(c => c.GetParameters().Length == ctorParams.Length);
            //assignments.Add(Expression.Assign(controlParameter, 
            //    Expression.New(
            //        control.Metadata.Type.GetConstructors().Single(c => c.GetParameters().Length == control.ConstructorParameters.Length),
            //        control.ConstructorParameters.Select(Expression.Constant))));
            var mc = new MethodContext();

            var state = new ExecutionState(setExpressions: assignments.ToImmutableDictionary(k => k.Left, k => k.Right, ExpressionComparer.Instance), conditions: conditions.ToImmutableList());

            state = Executor.CallCtor(controlCtor, state.WithStack(ctorParams.Select(Expression.Constant)));
            var thisParam = state.Stack.Single();
            //state = state.WithSet(controlParameter, StackConversion.ImplicitConvertTo(thisParam, controlParameter.Type));

            var propertiesField = Expression.Field(thisParam, "properties");
            var propertiesRoot = MyExpression.RootParameter(propertiesField.Type, "propertiesRoot", notNull: true, exactType: true);
            state = Executor.InitObject(state, propertiesRoot);
            state = state.WithSet(propertiesField, propertiesRoot).WithSet(propertiesRoot, Expression.New(typeof(Dictionary<DotvvmProperty, object>)));
            foreach (var property in control.Properties)
            {
                Expression value;
                if (property.Value is ResolvedPropertyValue)
                {
                    value = Expression.Constant((property.Value as ResolvedPropertyValue).Value);
                }
                else if (property.Value is ResolvedPropertyBinding)
                {
                    var binding = (ResolvedPropertyBinding)property.Value;
                    value = MyExpression.RootParameter(binding.Binding.BindingType, SymbolicExecutor.NameParam("bindingr"), exactType: true, notNull: true);
                }
                else throw new NotSupportedException();
                state = SpecialExecutors.Dictionary_InsertValue(Executor, state, new[] { propertiesRoot, Expression.Constant(property.Key), StackConversion.ImplicitConvertTo(value, typeof(object)).Simplify() }, null);
            }

            var attributes = typeof(IControlWithHtmlAttributes).IsAssignableFrom(control.Metadata.Type) ? Executor.CallMethod(typeof(IControlWithHtmlAttributes).GetProperty("Attributes").GetMethod, state, true).Stack.Single() : null;
            if (control.HtmlAttributes != null) foreach (var attribute in control.HtmlAttributes)
            {
                if (attributes == null) throw new Exception($"Control '{thisParam.Type}' does not support html attributes.");
                Expression value;
                if (attribute.Value is ResolvedBinding)
                {
                    value = MyExpression.RootParameter(attribute.Value.CastTo<ResolvedBinding>().BindingType, SymbolicExecutor.NameParam("bindingr"), exactType: true, notNull: true);
                }
                else
                {
                    value = Expression.Constant(attribute.Value);
                }
                state = SpecialExecutors.Dictionary_InsertValue(Executor, state, new[] { attributes, Expression.Constant(attribute.Key), StackConversion.ImplicitConvertTo(value, typeof(object)).Simplify() }, null);
            }


            state = Executor.CallMethod(typeof(DotvvmControl).GetMethod("set_Children", BindingFlags.NonPublic | BindingFlags.Instance), state.WithStack(new[] { thisParam, MyExpression.RootParameter(typeof(DotvvmControlCollection), "childControls") }), false);

            return state.WithStack(new[] { thisParam });
        }
    }
}
