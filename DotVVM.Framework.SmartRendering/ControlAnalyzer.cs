﻿using DotVVM.Framework.Compilation.ControlTree.Resolved;
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
using System.Net;
using System.Diagnostics;
using DotVVM.Framework.Binding.Expressions;

namespace DotVVM.Framework.SmartRendering
{
	public class InitializeControlResult
	{
		public ExecutionState ExecutionState { get; }
		public IReadOnlyDictionary<Expression, ResolvedBinding> BindingAssignment { get; }
		public IReadOnlyDictionary<Expression, ResolvedControl> ControlAssignment { get; }
		public Expression ControlParameter { get; }
		public Expression ChildControlsParameter { get; }

		public InitializeControlResult(ExecutionState state,
			IReadOnlyDictionary<Expression, ResolvedBinding> bindings,
			IReadOnlyDictionary<Expression, ResolvedControl> controls,
			Expression controlParameter,
			Expression childControlsParameter)
		{
			ExecutionState = state;
			BindingAssignment = bindings;
			ControlAssignment = controls;
			ControlParameter = controlParameter;
			ChildControlsParameter = childControlsParameter;
		}
	}
	public class ControlAnalyzer
	{
		public static readonly MyParameterExpression WriterParameter = MyExpression.RootParameter(typeof(IHtmlWriter), "writer");
		public static readonly MyParameterExpression ContextParameter = MyExpression.RootParameter(typeof(IDotvvmRequestContext), "dotvvmRequestContext");


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

		public ExecutionState ExecuteControlLifecycle(ExecutionState state)
		{
			var controlParameter = (MyParameterExpression)state.Stack.Single();
			foreach (var m in methods)
			{
				state = CallLifecycleMethod(state, m, controlParameter, ContextParameter);
			}
			state = Executor.CallMethod(typeof(DotvvmControl).GetMethod("Render"), state.WithStack(new[] { controlParameter, WriterParameter, ContextParameter }), true);
			return state.WithStack(new[] { controlParameter });
		}

		private ExecutionState CallLifecycleMethod(ExecutionState state, MethodInfo method, Expression control, Expression context) =>
			Executor.CallMethod(method, state.WithStack(new[] { control, context }), true);

		public InitializeControlResult CreateExecutionState(ResolvedControl control)
		{
			var ctorParams = control.ConstructorParameters ?? new object[0];
			var controlCtor = control.Metadata.Type.GetConstructors().Single(c => c.GetParameters().Length == ctorParams.Length);
			//assignments.Add(Expression.Assign(controlParameter, 
			//    Expression.New(
			//        control.Metadata.Type.GetConstructors().Single(c => c.GetParameters().Length == control.ConstructorParameters.Length),
			//        control.ConstructorParameters.Select(Expression.Constant))));

			var bindingAssignment = new Dictionary<Expression, ResolvedBinding>(ExpressionComparer.Instance);
			var controlAssignment = new Dictionary<Expression, ResolvedControl>(ExpressionComparer.Instance);

			var state = new ExecutionState();

			state = Executor.CallCtor(controlCtor, state.WithStack(ctorParams.Select(Expression.Constant)));
			var thisParam = state.Stack.Single();
			controlAssignment.Add(thisParam, control);
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
					bindingAssignment.Add(value, binding.Binding);
				}
				else throw new NotSupportedException();
				state = SpecialExecutors.Dictionary_InsertValue(Executor, state, new[] { propertiesRoot, Expression.Constant(property.Key), StackConversion.ImplicitConvertTo(value, typeof(object)).Simplify() }, null);
			}

			// TODO: property propertygroups
			var attributes = typeof(IControlWithHtmlAttributes).IsAssignableFrom(control.Metadata.Type) ? Executor.CallMethod(typeof(IControlWithHtmlAttributes).GetProperty("Attributes").GetMethod, state.WithStack(new[] { thisParam }), true).Stack.Single() : null;
			foreach (var attributeProp in control.Properties.Keys.OfType<GroupedDotvvmProperty>().Where(p => p.PropertyGroup.Prefixes.Contains("")))
				{
					var attribute = control.Properties[attributeProp];
					if (attributes == null) throw new Exception($"Control '{thisParam.Type}' does not support html attributes.");
					Expression value;
					if (attribute is ResolvedPropertyBinding propertyBinding)
					{
						var binding = propertyBinding.Binding;
						value = MyExpression.RootParameter(binding.BindingType, SymbolicExecutor.NameParam("bindingr"), exactType: true, notNull: true);
						bindingAssignment.Add(value, binding);
					}
					else
					{
						value = Expression.Constant(attribute.CastTo<ResolvedPropertyValue>().Value);
					}
					state = SpecialExecutors.Dictionary_InsertValue(Executor, state, new[] { attributes, Expression.Constant(attributeProp.GroupMemberName), StackConversion.ImplicitConvertTo(value, typeof(object)).Simplify() }, null);
				}


			var childControlsParameter = MyExpression.RootParameter(typeof(DotvvmControlCollection), "childControls");
			state = Executor.CallMethod(typeof(DotvvmControl).GetMethod("set_Children", BindingFlags.NonPublic | BindingFlags.Instance), state.WithStack(new[] { thisParam, childControlsParameter }), false);
			state = state.WithStack(new[] { thisParam });

			return new InitializeControlResult(state, bindingAssignment, controlAssignment, thisParam, childControlsParameter);
		}

		private static readonly MethodInfo RenderChilderMethod = typeof(DotvvmControl).GetMethod("RenderChildren", BindingFlags.Instance | BindingFlags.NonPublic);
		public ControlBehaviour AnalyzeControlBehaviour(ExecutionState state, InitializeControlResult controlInitialization, ResolvedControl control)
		{
			var thisControl = state.Stack[0];

			var requiredParameters = new HashSet<Expression>(ExpressionComparer.Instance);
			var output = new List<Expression>();
			var findParameterUsageVisitor = new FindParameterUsageVisitor() { ExecutionState = state };
			var currentAttributes = new List<RenderedAttribute>();
			var elementStack = new Stack<Expression>();

			var sadkhjaskdh = state.ToString();

			foreach (var effectKVP in state.SideEffects)
			{
				Debug.Assert(effectKVP.Key == null);
				var effect = effectKVP.Value;
				findParameterUsageVisitor.Parameters.Clear();
				findParameterUsageVisitor.Visit(effect);
				var usedParameters = findParameterUsageVisitor.Parameters;
				foreach (var p in usedParameters) requiredParameters.Add(p);

				Expression assignment = null;
				if (effect.NodeType == ExpressionType.Assign)
				{
					assignment = effect.CastTo<BinaryExpression>().Left;
					effect = effect.CastTo<BinaryExpression>().Right;

					output.Add(Expression.Block(
						Expression.Assign(assignment, effect),
						Expression.Default(typeof(void))));
				}

				else if (effect.NodeType == ExpressionType.Call)
				{
					var expressionCall = (MethodCallExpression)effect;
					var method = expressionCall.Method;
					var isWriterCall = effect.NodeType == ExpressionType.Call && ExpressionComparer.Instance.Equals(effect.CastTo<MethodCallExpression>().Object, WriterParameter);
					if (isWriterCall)
					{
						if (method.Name == nameof(IHtmlWriter.AddAttribute))
						{
							var name = expressionCall.Arguments[0]; // string
							var value = expressionCall.Arguments[1]; // string
							var append = expressionCall.Arguments[2]; // bool
							var appendSeparator = expressionCall.Arguments[3]; // string

							currentAttributes.Add(new RenderedAttribute(name, value));
						}
						else if (method.Name == nameof(IHtmlWriter.RenderBeginTag) || method.Name == nameof(IHtmlWriter.RenderSelfClosingTag))
						{
							var name = expressionCall.Arguments[0]; // string

							output.Add(Expression.Constant("<"));
							output.Add(name);
							output.AddRange(currentAttributes.SelectMany(c => c.CreateOutputParts()));
							if (method.Name == nameof(IHtmlWriter.RenderSelfClosingTag)) output.Add(Expression.Constant(" /"));
							else elementStack.Push(name);
							output.Add(Expression.Constant(">"));
						}
						else if (method.Name == nameof(IHtmlWriter.RenderEndTag))
						{
							var name = elementStack.Pop();
							output.Add(Expression.Constant("</"));
							output.Add(name);
							output.Add(Expression.Constant(">"));
						}
						else throw new NotSupportedException();
					}

					else if (method == RenderChilderMethod && ExpressionComparer.Instance.Equals(expressionCall.Object, thisControl))
					{
						// this.RenderChilden
						if (control.Content.Count > 0) output.Add(new RenderControlsExpression(control.Content.ToImmutableList()));
					}
					else throw new NotSupportedException();
				}
				else throw new NotSupportedException();
			}
			AssignmentInlining(output);
			output = AddThemTogether(output.SelectMany(ExpandExpressions)).ToList();
			var formattedOutput = FormatOutputExpression(output, state);
			var visitor = new ResultExpressionSimplifiingVisitor() { ControlInitialization = controlInitialization };
			var renderedOutput = output.Select(e => CreateOutputFromExpression(e, controlInitialization, control, visitor)).ToArray();
			return new ControlBehaviour(new ParameterExpression[] { }, Expression.Default(typeof(void)), renderedOutput, new Expression[] { });
		}

		private static RenderedOutput CreateOutputFromExpression(Expression expression, InitializeControlResult controlInitialization, ResolvedControl control, ResultExpressionSimplifiingVisitor simplifiingVisitor)
		{
			if (expression is RenderControlsExpression)
			{
				return new RenderControls(expression.CastTo<RenderControlsExpression>().Controls);
			}
			if (expression.IsConstant())
			{
				return new RenderedText(expression.GetConstantValue().ToString());
			}
			{

			}
			expression = simplifiingVisitor.Visit(expression);
			return new RenderExpressionValue(expression);
		}

		private static IEnumerable<Expression> ExpandExpressions(Expression e)
		{
			if (e is RenderControlsExpression || e.IsConstant()) { return new[] { e }; }

			if (e.NodeType == ExpressionType.Call)
			{
				var methodCallExpression = e.CastTo<MethodCallExpression>();
				if (methodCallExpression.Method.Name == nameof(WebUtility.HtmlEncode) && methodCallExpression.Method?.DeclaringType == typeof(WebUtility) && methodCallExpression.Arguments.Single().NodeType == ExpressionType.Add)
				{
					var binaryExpression = methodCallExpression.Arguments.Single() as BinaryExpression;
					return ExpandExpressions(binaryExpression.Update(methodCallExpression.Update(null, new[] { binaryExpression.Left }), binaryExpression.Conversion, methodCallExpression.Update(null, new[] { binaryExpression.Right })).Simplify(evaluateFunctions: true));
				}
			}
			if (e.NodeType == ExpressionType.Add && e.Type == typeof(string))
			{
				return ExpandExpressions(e.CastTo<BinaryExpression>().Left).Concat(ExpandExpressions(e.CastTo<BinaryExpression>().Right));
			}
			return new[] { e };
		}

		private static IEnumerable<Expression> AddThemTogether(IEnumerable<Expression> exprs)
		{
			string currentString = null;
			foreach (var effff in exprs)
			{
				var e = effff;
				if (e is RenderControlsExpression) yield return e;
				else if (e.IsConstant()) currentString += e.GetConstantValue().ToString();
				else
				{
					if (!string.IsNullOrEmpty(currentString)) { yield return Expression.Constant(currentString); currentString = null; }
					//if (e.NodeType == ExpressionType.Call)
					//{
					//	var methodCallExpression = e.CastTo<MethodCallExpression>();
					//	if (methodCallExpression.Method.Name == nameof(WebUtility.HtmlEncode) && methodCallExpression.Method?.DeclaringType == typeof(WebUtility) && methodCallExpression.Arguments.Single().NodeType == ExpressionType.Add)
					//	{
					//		var binaryExpression = methodCallExpression.Arguments.Single() as BinaryExpression;
					//		e = binaryExpression.Update(methodCallExpression.Update(null, new[] { binaryExpression.Left }), binaryExpression.Conversion, methodCallExpression.Update(null, new[] { binaryExpression.Right })).Simplify(evaluateFunctions: true);
					//	}
					//}
					//if (e.NodeType == ExpressionType.Add && e.Type == typeof(string))
					//{
					//	// ` [e cast(BinaryExpression) dup .Left, .Right] AddThemTogether() foreach { yield }
					//	foreach (var rr in AddThemTogether(new[] { e.CastTo<BinaryExpression>().Left, e.CastTo<BinaryExpression>().Right }))
					//		yield return rr;
					//}
					yield return e;
				}
			}
			if (!string.IsNullOrEmpty(currentString)) yield return Expression.Constant(currentString);
		}

		private static void AssignmentInlining(List<Expression> exprs)
		{
			var paramUsage = new Dictionary<Expression, int>(ExpressionComparer.Instance);
			var paramAssignment = new Dictionary<Expression, InlineVarPlacement>(ExpressionComparer.Instance);
			foreach (var e in exprs)
			{
				if (e is RenderControlsExpression) continue;
				if (e is BlockExpression)
				{
					var block = e.CastTo<BlockExpression>();
					if (block.Type == typeof(void))
					{
						foreach (var s in block.Expressions)
						{
							if (s.NodeType == ExpressionType.Assign)
							{
								var bop = (BinaryExpression)s;
								if (bop.Left is MyParameterExpression)
								{
									CountParameterUsage(bop.Right, paramUsage);
									paramUsage[bop.Left] = 0;
									paramAssignment[bop.Left] = new InlineVarPlacement(bop.Right, block, s);
									continue;
								}
							}
							CountParameterUsage(s, paramUsage);
						}
					}
					else CountParameterUsage(block, paramUsage);
				}
				else CountParameterUsage(e, paramUsage);
			}
			var replacer = new ExpressionReplaceVisitor();
			foreach (var p in paramUsage)
			{
				if (p.Value == 1 && paramAssignment.ContainsKey(p.Key))
				{
					var place = paramAssignment[p.Key];
					replacer.Replace.Add(p.Key, new Lazy<Expression>(() => place.Assignment));

					if (place.BlockExpression == null) exprs.Remove(place.RemoveExpr);
					else
					{
						var bi = exprs.IndexOf(place.RemoveExpr);
						var block = exprs[bi].CastTo<BlockExpression>();
						var blockExpressions = block.Expressions.ToList();
						blockExpressions.Remove(place.BlockExpression);
						if (blockExpressions.Count == 1 && ExpressionComparer.Instance.Equals(blockExpressions[0], Expression.Default(typeof(void))))
							exprs.RemoveAt(bi);
						else
							exprs[bi] = block.Update(block.Variables, blockExpressions);
					}
				}
			}

			for (int i = 0; i < exprs.Count; i++)
			{
				if (exprs[i] is RenderControlsExpression) continue;
				exprs[i] = replacer.Visit(exprs[i]);
			}
		}

		struct InlineVarPlacement
		{
			public readonly Expression Assignment;
			public readonly Expression RemoveExpr;
			public readonly Expression BlockExpression;

			public InlineVarPlacement(Expression assignment, Expression removeExpr, Expression blockExpression)
			{
				Assignment = assignment;
				RemoveExpr = removeExpr;
				BlockExpression = blockExpression;
			}
		}

		static void CountParameterUsage(Expression expr, Dictionary<Expression, int> paramUsage)
		{
			expr.WalkTree(e =>
			{
				int o;
				if (paramUsage.TryGetValue(e, out o)) paramUsage[e] = o + 1;
			});
		}

		private static string FormatOutputExpression(IEnumerable<Expression> output, ExecutionState state)
		{
			return string.Concat(output.Select(o =>
			{
				if (o.IsConstant()) return o.GetConstantValue().ToString();
				else if (o is RenderControlsExpression) return $"{{Controls}}";
				else return "{" + o.WalkTree(e =>
				{
					if (state.SetExpressions.Keys.Count(k => k.Contains(e)) > 1) return null;
					return state.TryFindAssignment(e);
				}, rewalkOnReplace: true).ToString() + "}";
			}));
		}

		RenderedOutput GetRenderedOutput(ExecutionState state, Expression expression)
		{
			if (expression.IsConstant()) return new RenderedText(expression.GetConstantValue().ToString());
			return new RenderExpressionValue(expression.CallToString());
		}

		class RenderedAttribute
		{
			public Expression Name { get; }
			public Expression Value { get; }

			public RenderedAttribute(Expression name, Expression value)
			{
				this.Name = name;
				this.Value = value;
			}

			public IEnumerable<Expression> CreateOutputParts()
			{
				// TODO: value joining (at least for names), symbolic null
				if (Value == null || (Value.IsConstant() && Value.GetConstantValue() == null)) return new[] { Expression.Constant(" "), Name };
				return new[] { Expression.Constant(" "), Name, Expression.Constant("=\""), EscapeAttribute(Value), Expression.Constant("\"") };
			}
			public static readonly MethodInfo EncodeMethod = typeof(WebUtility).GetMethod(nameof(WebUtility.HtmlEncode), BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
			public static Expression EscapeAttribute(Expression value)
			{
				if (value.IsConstant()) return Expression.Constant(WebUtility.HtmlEncode(value.GetConstantValue().ToString()));
				else
				{
					var resultType = value.Type;
					if (resultType.IsNumericType() || resultType == typeof(bool)) return value; // numeric value can't contain bad chars
					return Expression.Call(EncodeMethod, value.CallToString());
				}
				throw new NotSupportedException();
			}
		}
		class FindParameterUsageVisitor : SymbolicInterpreter.ExpressionVisitor
		{
			public ExecutionState ExecutionState { get; set; }
			public HashSet<Expression> Parameters { get; set; } = new HashSet<Expression>(ExpressionComparer.Instance);

			public override Expression Visit(Expression node)
			{
				if (node != null && ExecutionState.TryFindAssignment(node) != null) { Parameters.Add(node); }
				return base.Visit(node);
			}

			protected override Expression VisitParameter(ParameterExpression node)
			{
				Parameters.Add(node);
				return base.VisitParameter(node);
			}

			protected override Expression VisitMyParameter(MyParameterExpression parameter)
			{
				Parameters.Add(parameter);
				return base.VisitMyParameter(parameter);
			}
		}
	}
}
