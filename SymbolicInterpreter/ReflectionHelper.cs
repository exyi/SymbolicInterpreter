using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{

    public static class ReflectionHelper
    {
        public static MethodInfo TransformGenericMethod(this MethodInfo method, params Type[] types)
        {
            var genMethod = method.GetGenericMethodDefinition();

            var parameters = genMethod.GetGenericArguments();
            var results = new Type[parameters.Length];
            var args = genMethod.GetParameters();
            for (int i = 0; i < args.Length; i++)
            {
                GenericLookup(parameters, results, args[i].ParameterType, types[i]);
            }
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] == null)
                {
                    results[i] = method.GetGenericArguments()[i];
                }
            }
            return genMethod.MakeGenericMethod(results);
        }

        private static void GenericLookup(Type[] parameters, Type[] results, Type genericType, Type transformTo)
        {
            if (genericType.IsGenericParameter)
            {
                var index = Array.IndexOf(parameters, genericType);
                Debug.Assert(index >= 0);
                if (results[index] == null)
                    results[index] = transformTo;
                else if (results[index] != transformTo)
                    throw new Exception();
            }
            if (genericType.IsGenericType)
            {
                Debug.Assert(genericType.GetGenericTypeDefinition() == transformTo.GetGenericTypeDefinition());
                foreach (var item in genericType.GetGenericArguments().Zip(transformTo.GetGenericArguments(), (a, b) =>
                {
                    GenericLookup(parameters, results, a, b);
                    return false;
                })) ;
            }
        }

        public static Type ToGenericDefinition(this Type type) => type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;

        public static IEnumerable<Type> BaseTypesAndSelf(this Type type)
        {
            do
            {
                yield return type;
                type = type.BaseType;
            } while (type != null);
        }

        private static readonly HashSet<Type> NumericTypes = new HashSet<Type>()
        {
            typeof (sbyte),
            typeof (byte),
            typeof (short),
            typeof (ushort),
            typeof (int),
            typeof (uint),
            typeof (long),
            typeof (ulong),
            typeof (char),
            typeof (float),
            typeof (double),
            typeof (decimal)
        };

        public static bool IsNumericType(this Type type)
        {
            return NumericTypes.Contains(type);
        }
    }

}
