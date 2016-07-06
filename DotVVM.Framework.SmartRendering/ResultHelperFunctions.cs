using DotVVM.Framework.Binding;
using DotVVM.Framework.Compilation.ControlTree.Resolved;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DotVVM.Framework.SmartRendering
{
	public static class ResultHelperFunctions
	{
		public static MethodInfo EvaluateBindingMethod = typeof(ResultHelperFunctions).GetMethod("EvaluateBinding", BindingFlags.Public | BindingFlags.Static);
		public static object EvaluateBinding(ResolvedBinding binding, ResolvedControl contextControl, DotvvmProperty property)
		{
			throw new NotImplementedException();
		}
	}
}
