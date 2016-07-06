using SymbolicInterpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;
using DotVVM.Framework.Binding.Expressions;
using DotVVM.Framework.Binding;
using DotVVM.Framework.Compilation.ControlTree.Resolved;

namespace DotVVM.Framework.SmartRendering
{
	public class ResultExpressionSimplifiingVisitor: SymbolicInterpreter.ExpressionVisitor
	{
		public InitializeControlResult ControlInitialization { get; set; }
		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			if (node.Method.Name == nameof(IStaticValueBinding.Evaluate) && node.Method.DeclaringType == typeof(IStaticValueBinding))
			{
				// binding value
				var bindingParameter = node.Object.UnwrapConverts();
				var binding = ControlInitialization.BindingAssignment.FindOrDefault(bindingParameter);
				var controlParameter = node.Arguments[0].UnwrapConverts();
				var contextControl = ControlInitialization.ControlAssignment.FindOrDefault(controlParameter);
				var contextProperty = node.Arguments[1].UnwrapConverts();
				if (binding != null && contextControl != null && contextProperty.IsConstant())
				{
					return Expression.Call(ResultHelperFunctions.EvaluateBindingMethod, Expression.Constant(binding, typeof(ResolvedBinding)), Expression.Constant(contextControl, typeof(ResolvedControl)), contextProperty);
				}
			}
			return base.VisitMethodCall(node);
		}
	}
}
