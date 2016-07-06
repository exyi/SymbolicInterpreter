using DotVVM.Framework.Binding;
using DotVVM.Framework.Binding.Expressions;
using DotVVM.Framework.Controls;
using DotVVM.Framework.Hosting;
using SymbolicInterpreter;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DotVVM.Framework.SmartRendering
{
    public static class DotvvmSpecialExecutors
    {
        private static bool initialized;
        public static void RegisterAll()
        {
            if (initialized) return;
            initialized = true;
            MethodAnalyzer.SpecialExecutors.Add(typeof(DotvvmProperty).GetMethod("GetValue"), DotvvmSpecialExecutors.DotvvmProperty_GetValue);
            MethodAnalyzer.SpecialExecutors.Add(typeof(IStaticValueBinding).GetMethod("Evaluate"), DotvvmSpecialExecutors.IStaticValueBinding_Evaluate);
            MethodAnalyzer.AddResultInterface(typeof(IHtmlWriter));
            MethodAnalyzer.AddResultInterface(typeof(IDotvvmRequestContext));
            MethodAnalyzer.AddResultInterface(typeof(IStaticValueBinding));
            MethodAnalyzer.AddResultInterface(typeof(IValueBinding));
            MethodAnalyzer.AddResultInterface(typeof(DotvvmControl), m => m.Name == "RenderChildren");
            MethodAnalyzer.SpecialExecutors.Add(typeof(DotvvmBindableObject).GetMethod("GetDeclaredProperties", BindingFlags.Instance | BindingFlags.NonPublic), DotvvmControl_GetDeclaredProperties);
            //MethodAnalyzer.SpecialExecutors.Add(typeof(DotvvmBindableObject).GetProperty(nameof(DotvvmBindableObject.GetClosestWithPropertyValue)).GetMethod, DotvvmControl_GetDeclaredProperties);
            MethodAnalyzer.RegisterAlternateImplementation(typeof(DotvvmBindableObject).GetMethod("GetValue"), typeof(DotvvmSpecialExecutors).GetMethod("DotvvmControl_GetValue_AI", BindingFlags.Static | BindingFlags.NonPublic));
            MethodAnalyzer.RegisterAlternateImplementation(typeof(HtmlGenericControl).GetMethod("AddHtmlAttribute", BindingFlags.NonPublic | BindingFlags.Instance), typeof(DotvvmSpecialExecutors).GetMethod("HtmlGenericControl_AddHtmlAttribute_AI", BindingFlags.Static | BindingFlags.NonPublic));
            MethodAnalyzer.RegisterAlternateImplementation(typeof(IHtmlWriter).GetMethod(nameof(IHtmlWriter.AddKnockoutDataBind), BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(string) }, null), typeof(DotvvmSpecialExecutors).GetMethod("IHtmlWriter_AddKnockoutDataBind_StringExpression_AI", BindingFlags.Static | BindingFlags.NonPublic));
            MethodAnalyzer.RegisterAlternateImplementation(typeof(IHtmlWriter).GetMethod(nameof(IHtmlWriter.AddKnockoutDataBind), BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(KnockoutBindingGroup) }, null), typeof(DotvvmSpecialExecutors).GetMethod("IHtmlWriter_AddKnockoutDataBind_BindingGroup_AI", BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static ExecutionState IStaticValueBinding_Evaluate(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
        {
            var binding = parameters[0];
            var control = parameters[1];
            var property = parameters[2];

            state = exe.AddResultEffect(state.ReplaceTopStack(3, null), true, Expression.Call(binding, (MethodInfo)methodInfo, control, property));
            return state;
        }

        private static FieldInfo DotvvmBindableObject_properties = typeof(DotvvmBindableObject).GetField("properties", BindingFlags.Instance | BindingFlags.NonPublic);
        internal static ExecutionState DotvvmProperty_GetValue(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
        {
            var property = parameters[0]; // DotvvmProperty
            var bindableObject = parameters[1];
            var inherit = parameters[2].Resolve(state, true);

            var propertiesField = Expression.Field(bindableObject, DotvvmBindableObject_properties).Resolve(state);
            var propertyIndexerAccess = GetDictionaryIndexer(propertiesField, property);

            var assignment = state.TryFindAssignment(propertyIndexerAccess);
            if (assignment != null) return state.WithStack(new[] { assignment });

            if (property.IsConstant())
            {
                var propertyConstant = (DotvvmProperty)property.GetConstantValue();
                if (propertyConstant.IsValueInherited)
                {
                    exe.AddResultEffect(state, true, Expression.Call(property, (MethodInfo)methodInfo, bindableObject, inherit));
                }
                return state.WithStack(new[] { Expression.Constant(propertyConstant.DefaultValue, typeof(object)) });
            }
            throw new NotImplementedException();
        }


        private static object DotvvmControl_GetValue_AI(DotvvmBindableObject control, DotvvmProperty property, bool inherit)
        {
            var value = property.GetValue(control, inherit);
            if (property.IsBindingProperty) return value;
            if (value is IBinding)
            {
                if (inherit && !property.IsSet(control, false))
                {
                    int n;
                    control = control.GetClosestWithPropertyValue(out n, d => property.IsSet(d, false));
                }
                if (value is IStaticValueBinding)
                {
                    // handle binding
                    var binding = (IStaticValueBinding)value;
                    value = binding.Evaluate(control, property);
                }
                else if (value is CommandBindingExpression)
                {
                    var binding = (CommandBindingExpression)value;
                    value = binding.GetCommandDelegate(control, property);
                }
            }
            return value;
        }

        static void HtmlGenericControl_AddHtmlAttribute_AI(HtmlGenericControl control, IHtmlWriter writer, string name, object value)
        {
            var asstaticValueBinding = value as IStaticValueBinding;
            var asString = value as string;
            if (value == null)
                writer.AddAttribute(name, null);
            else if (asString != null)
                writer.AddAttribute(name, asString);
            else if (asstaticValueBinding != null)
                writer.AddAttribute(name, (string)asstaticValueBinding.Evaluate(control, null));
        }

		static void IHtmlWriter_AddKnockoutDataBind_StringExpression_AI(IHtmlWriter writer, string key, string expression)
		{
			writer.AddAttribute("data-bind", key + ":" + expression);
		}

		static void IHtmlWriter_AddKnockoutDataBind_BindingGroup_AI(IHtmlWriter writer, string key, KnockoutBindingGroup bindingGroup)
		{
			if (bindingGroup.IsEmpty) return;
			writer.AddAttribute("data-bind", key + ":" + bindingGroup.ToString());
		}

		public static Expression GetDictionaryIndexer(Expression target, Expression index)
            => Expression.MakeIndex(target, target.Type.GetProperty("Item"), new[] { index });

        public static ExecutionState DotvvmControl_GetDeclaredProperties(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
        {
            Debug.Assert(parameters.Count == 1);
            var thisType = parameters[0].ProveType(state);
            if (thisType == null) throw new NotImplementedException();
            var props = DotvvmProperty.ResolveProperties(thisType);
            return state.WithStack(new[] { Expression.Constant(props) });
        }
		
    }
}
