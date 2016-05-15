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
            MethodAnalyzer.specialExecutors.Add(typeof(DotvvmProperty).GetMethod("GetValue"), DotvvmSpecialExecutors.DotvvmProperty_GetValue);
            MethodAnalyzer.AddResultInterface(typeof(IHtmlWriter));
            MethodAnalyzer.AddResultInterface(typeof(IDotvvmRequestContext));
            MethodAnalyzer.AddResultInterface(typeof(IStaticValueBinding));
            MethodAnalyzer.AddResultInterface(typeof(IValueBinding));
            MethodAnalyzer.AddResultInterface(typeof(DotvvmControl), m => m.Name == "RenderChildren");
            MethodAnalyzer.specialExecutors.Add(typeof(DotvvmBindableObject).GetMethod("GetDeclaredProperties", BindingFlags.Instance | BindingFlags.NonPublic), DotvvmControl_GetDeclaredProperties);
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
