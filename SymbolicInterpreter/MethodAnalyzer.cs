using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public delegate ExecutionState MethodExecutor(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo);
    public static class MethodAnalyzer
    {
        public static bool IsPure(MethodBase mi)
        {
            if (Attribute.IsDefined(mi, typeof(PureAttribute))) return true;
            if (Attribute.IsDefined(mi.DeclaringType, typeof(PureAttribute))) return true;
            var dt = mi.DeclaringType;
            if (dt == typeof(string) || dt.IsNumericType() || dt == typeof(DateTime) || dt.IsEnum) return true;
            return false;
        }

        private static readonly Dictionary<Type, Func<MethodBase, bool>> resultInterfaces = new Dictionary<Type, Func<MethodBase, bool>> { };

        public static void AddResultInterface(Type type, Func<MethodBase, bool> condition = null)
        {
            Func<MethodBase, bool> oldcondition;
            if (resultInterfaces.TryGetValue(type, out oldcondition) && oldcondition != null)
            {
                if(condition != null) resultInterfaces[type] = f => oldcondition(f) && condition(f);
            }
            else
            {
                resultInterfaces.Add(type, condition);
            }
        }

        public static bool IsResultEffect(MethodBase mi)
        {
            if (mi.MethodImplementationFlags.HasFlag(MethodImplAttributes.Native) || mi.MethodImplementationFlags.HasFlag(MethodImplAttributes.InternalCall)) return true;
            Func<MethodBase, bool> condition;
            if (resultInterfaces.TryGetValue(mi.DeclaringType, out condition) && condition?.Invoke(mi) != false) return true;
            foreach (var ifc in resultInterfaces)
            {
                if (ifc.Key.IsAssignableFrom(mi.DeclaringType) && ifc.Key.IsInterface)
                {
                    var map = mi.DeclaringType.GetInterfaceMap(ifc.Key);
                    var methodindex = Array.IndexOf(map.TargetMethods, mi);
                    if (methodindex >= 0)
                    {
                        var ifcm = map.InterfaceMethods[methodindex];
                        return ifc.Value(ifcm);
                    }
                }
            }
            return false;
        }

        public static Dictionary<MethodBase, MethodExecutor> specialExecutors = new Dictionary<MethodBase, MethodExecutor>
        {
            { typeof(IDictionary<,>).GetProperty("Item").SetMethod, SpecialExecutors.Dictionary_SetValue }
        };
        public static MethodExecutor GetSpecialExecutor(MethodBase minfo, IList<Expression> parameters)
        {
            if (minfo == null) return null;
            MethodExecutor result = null;
            if (specialExecutors.TryGetValue(minfo, out result))
                return result;
            if (!minfo.DeclaringType.IsInterface)
            {
                foreach (var ifc in minfo.DeclaringType.GetInterfaces())
                {
                    var ifcMap = minfo.DeclaringType.GetInterfaceMap(ifc);
                    var mIndex = Array.IndexOf(ifcMap.TargetMethods, minfo);
                    if (mIndex >= 0)
                    {
                        result = GetSpecialExecutor(ifcMap.InterfaceMethods[mIndex], parameters);
                        if (result != null)
                            return result;
                    }
                }
            }
            if (minfo.DeclaringType.IsGenericType && !minfo.DeclaringType.IsGenericTypeDefinition)
            {
                var gtd = minfo.DeclaringType.GetGenericTypeDefinition();
                result = GetSpecialExecutor(gtd.GetMethod(minfo.Name), parameters);
                if (result != null) return result;
            }
            return null;
        }

        public static bool IsStandardCtor(ConstructorInfo ctor)
        {
            if (ctor.DeclaringType.Assembly.GetName().Name == "mscorlib") return true;
            return false;
        }
    }

    static class SpecialExecutors
    {

        internal static ExecutionState Dictionary_SetValue(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
        {
            return state.WithSet(GetDictionaryIndexer(parameters[0], parameters[1]), parameters[2]);
        }


        private static Expression GetDictionaryIndexer(Expression target, Expression index)
            => Expression.MakeIndex(target, target.Type.GetProperty("Item"), new[] { index });
    }
}
