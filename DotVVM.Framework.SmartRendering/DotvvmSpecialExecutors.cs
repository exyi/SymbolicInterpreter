using DotVVM.Framework.Binding;
using DotVVM.Framework.Controls;
using DotVVM.Framework.Hosting;
using SymbolicInterpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DotVVM.Framework.SmartRendering
{
    public static class DotvvmSpecialExecutors
    {
        static DotvvmSpecialExecutors()
        {
            MethodAnalyzer.specialExecutors.Add(typeof(DotvvmProperty).GetMethod("GetValue"), DotvvmSpecialExecutors.DotvvmProperty_GetValue);
            MethodAnalyzer.AddResultInterface(typeof(IHtmlWriter));
            MethodAnalyzer.AddResultInterface(typeof(IDotvvmRequestContext));
            MethodAnalyzer.AddResultInterface(typeof(DotvvmControl), m => m.Name == "RenderChildren");
        }

        public static void RegisterAll()
        {

        }


        private static FieldInfo DotvvmBindableObject_properties = typeof(DotvvmBindableObject).GetField("properties");
        internal static ExecutionState DotvvmProperty_GetValue(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
        {
            var property = parameters[0]; // DotvvmProperty
            var bindableObject = parameters[1];
            var inherit = parameters[2].Resolve(state, true);

            var propertiesField = Expression.Field(bindableObject, DotvvmBindableObject_properties);
            var propertyIndexerAccess = GetDictionaryIndexer(propertiesField, property);

            var assignment = state.TryFindAssignment(propertyIndexerAccess);
            if (assignment != null) state.WithStack(new[] { assignment });

            if (property.IsConstant())
            {
                var propertyConstant = (DotvvmProperty)property.GetConstantValue();
                if (propertyConstant.IsValueInherited)
                {
                    throw new NotSupportedException();
                }
                return state.WithStack(new[] { Expression.Constant(propertyConstant.DefaultValue, typeof(object)) });
            }
            throw new NotImplementedException();
        }

        private static Expression GetDictionaryIndexer(Expression target, Expression index)
            => Expression.MakeIndex(target, target.Type.GetProperty("Item"), new[] { index });
    }
}
