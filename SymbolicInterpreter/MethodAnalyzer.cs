using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
	public delegate ExecutionState MethodExecutor(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo);
	public static class MethodAnalyzer
	{
		static MethodAnalyzer()
		{
			RegisterAlternateImplementation(
				typeof(string).GetMethod("Join", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(string), typeof(string[]), typeof(int), typeof(int) }, null),
				typeof(SpecialExecutors).GetMethod(nameof(SymbolicInterpreter.SpecialExecutors.String_Join_Strings_AI), BindingFlags.Static | BindingFlags.Public));
			RegisterAlternateImplementation(
				typeof(string).GetMethod(nameof(String.Join), BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string), typeof(object[]) }, null),
				typeof(SpecialExecutors).GetMethod(nameof(SymbolicInterpreter.SpecialExecutors.String_Join_ObjectArray_AI), BindingFlags.Static | BindingFlags.Public));
			RegisterAlternateImplementation(
				typeof(string).GetMethod(nameof(String.Join), BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string), typeof(IEnumerable<string>) }, null),
				typeof(SpecialExecutors).GetMethod(nameof(SymbolicInterpreter.SpecialExecutors.String_Join_string_IEnumerable_AI), BindingFlags.Static | BindingFlags.Public));
			RegisterAlternateImplementation(
				typeof(string).GetMethods(BindingFlags.Static | BindingFlags.Public).Where(m => m.Name == "Join" && m.GetParameters().Length == 2 && m.IsGenericMethod).Single(),
				typeof(SpecialExecutors).GetMethod(nameof(SymbolicInterpreter.SpecialExecutors.String_Join_T_IEnumerable_AI), BindingFlags.Static | BindingFlags.Public));
		}
		private static HashSet<string> stringPureMethods = new HashSet<string> { nameof(string.Join) };
		public static bool IsPure(MethodBase mi)
		{
			if (Attribute.IsDefined(mi, typeof(PureAttribute))) return true;
			if (Attribute.IsDefined(mi.DeclaringType, typeof(PureAttribute))) return true;
			var dt = mi.DeclaringType;
			if ((dt == typeof(string) && !stringPureMethods.Contains(mi.Name)) || dt.IsNumericType() || dt == typeof(DateTime) || dt.IsEnum || typeof(MemberInfo).IsAssignableFrom(dt)) return true;
			return false;
		}

		private static readonly Dictionary<Type, Func<MethodBase, bool>> resultInterfaces = new Dictionary<Type, Func<MethodBase, bool>> { };

		public static void AddResultInterface(Type type, Func<MethodBase, bool> condition = null)
		{
			Func<MethodBase, bool> oldcondition;
			if (resultInterfaces.TryGetValue(type, out oldcondition) && oldcondition != null)
			{
				if (condition != null) resultInterfaces[type] = f => oldcondition(f) && condition(f);
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

		public static Dictionary<MethodBase, MethodExecutor> SpecialExecutors = new Dictionary<MethodBase, MethodExecutor>(ExpressionComparer.Instance)
		{
			{ typeof(IDictionary<,>).GetMethod("Add"), SymbolicInterpreter.SpecialExecutors.Dictionary_InsertValue },
			{ typeof(Dictionary<,>).GetMethod("Insert", BindingFlags.Instance | BindingFlags.NonPublic), SymbolicInterpreter.SpecialExecutors.Dictionary_InsertValue },
			{ typeof(IDictionary<,>).GetProperty("Item").GetMethod, SymbolicInterpreter.SpecialExecutors.Dictionary_GetValue },
			{ typeof(IDictionary<,>).GetProperty("Item").SetMethod, SymbolicInterpreter.SpecialExecutors.Dictionary_InsertValue },
			{ typeof(IDictionary<,>).GetMethod("ContainsKey"), SymbolicInterpreter.SpecialExecutors.Dictionary_ContainsKey },
			{ typeof(IDictionary<,>).GetMethod("TryGetValue"), SymbolicInterpreter.SpecialExecutors.Dictionary_TryGetValue },
			{ typeof(IEnumerable<>).GetMethod("GetEnumerator"), SymbolicInterpreter.SpecialExecutors.Generic_GetEnumerator },
			{ typeof(Dictionary<,>.Enumerator).GetMethod("MoveNext"), SymbolicInterpreter.SpecialExecutors.Dictionary_Enumerator_MoveNext },
			{ typeof(Exception).GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Instance), SymbolicInterpreter.SpecialExecutors.DoNothing },
			{ typeof(Dictionary<,>).GetConstructors().Single(c => c.GetParameters().Length == 2 && c.GetParameters()[0].ParameterType == typeof(int)), SymbolicInterpreter.SpecialExecutors.Dictionary_Ctor },
			{ typeof(object).GetMethod("GetType"), SymbolicInterpreter.SpecialExecutors.Object_GetType }
		};

		public static void RegisterAlternateImplementation(MethodBase methodToReplace, MethodBase alternativeImplementation)
		{
			Contract.Requires(methodToReplace != null);
			Contract.Requires(alternativeImplementation != null);
			if (methodToReplace == null) throw new ArgumentNullException(nameof(methodToReplace));
			if (alternativeImplementation == null) throw new ArgumentNullException(nameof(alternativeImplementation));
			SpecialExecutors[methodToReplace] = (exe, state, parameters, methodInfo) =>
			{
				var aicp = alternativeImplementation;
				if (methodInfo.IsGenericMethod && alternativeImplementation.IsGenericMethodDefinition)
				{
					aicp = ((MethodInfo)alternativeImplementation).MakeGenericMethod(methodInfo.GetGenericArguments());
				}
				return exe.CallMethod(aicp, state, false);
			};
		}

		public static MethodExecutor GetSpecialExecutor(MethodBase minfo, IList<Expression> parameters)
		{
			if (minfo == null) return null;
			MethodExecutor result = null;
			if (SpecialExecutors.TryGetValue(minfo, out result))
				return result;
			if (!minfo.DeclaringType.IsInterface && minfo is MethodInfo)
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
			var ginfo = Ungenericise(minfo);
			if (ginfo != minfo)
			{
				result = GetSpecialExecutor(ginfo, parameters);
				if (result != null) return result;
			}
			if (minfo.DeclaringType == typeof(string) && minfo.Name == "Concat")
				return SymbolicInterpreter.SpecialExecutors.String_Concat;
			if (minfo.Name == "Invoke" && typeof(Delegate).IsAssignableFrom(minfo.DeclaringType))
				return SymbolicInterpreter.SpecialExecutors.DelegateInvoke;
			return null;
		}

		static MethodBase Ungenericise(MethodBase mi)
		{
			if (mi is MethodInfo) return Ungenericise((MethodInfo)mi);
			if (mi is ConstructorInfo) return Ungenericise((ConstructorInfo)mi);
			Debug.Assert(false);
			return mi;
		}
		static ConstructorInfo Ungenericise(ConstructorInfo ci)
		{
			var unt = Ungenericise(ci.DeclaringType);
			if (unt == ci.DeclaringType) return ci;
			return unt.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Single(m => m.MetadataToken == ci.MetadataToken);

		}
		static MethodInfo Ungenericise(MethodInfo mi)
		{
			if (mi.IsGenericMethod) mi = mi.GetGenericMethodDefinition();
			var unt = Ungenericise(mi.DeclaringType);
			if (unt == mi.DeclaringType) return mi;
			return
				unt.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Single(m => m.MetadataToken == mi.MetadataToken);
		}

		static Type Ungenericise(Type type)
		{
			if (type.DeclaringType != null)
			{
				return Ungenericise(type.DeclaringType).GetNestedType(type.Name, BindingFlags.Public | BindingFlags.NonPublic);
			}
			else if (type.IsGenericType) return type.GetGenericTypeDefinition();
			else return type;
		}

		public static bool IsConstantField(FieldInfo field, out object value)
		{
			if (typeof(Delegate).IsAssignableFrom(field.FieldType) && Attribute.IsDefined(field.DeclaringType, typeof(CompilerGeneratedAttribute)) && field.Name.Contains("<"))
			{
				value = null;
				return true;
			}
			if (field.IsInitOnly && (field.FieldType.IsValueType ||
				field.FieldType.Name == "DotvvmProperty" ||
				typeof(Expression).IsAssignableFrom(field.FieldType) ||
				field.FieldType.Name.Contains("Immutable") || // trust the name ...
				(field.FieldType.IsArray && ((Array)field.GetValue(null)).Length == 0)
				))
			{
				value = field.GetValue(null);
				return true;
			}
			value = null;
			return false;
		}
	}

	public static class SpecialExecutors
	{

		public static ExecutionState DoNothing(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			if (methodInfo is MethodInfo && ((MethodInfo)methodInfo).ReturnType != typeof(void))
				return state.WithStack(new[] { Expression.Default(((MethodInfo)methodInfo).ReturnType).Simplify() });
			else return state.ClearStack();
		}

		public static ExecutionState DelegateInvoke(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			var d = state.TryFindAssignment(parameters[0]);
			Debug.Assert(d.NodeType == ExpressionType.New && d.Type == methodInfo.DeclaringType);
			var initargs = ((NewExpression)d).Arguments;
			var context = initargs[0];
			Debug.Assert(initargs[1] is FunctionPointerExpression);
			var method = initargs[1].CastTo<FunctionPointerExpression>().Method;
			return exe.CallMethod(method, state.WithStack((context != null ? new[] { context } : new Expression[0]).Concat(parameters.Skip(1))), true);
		}

		public static ExecutionState Dictionary_InsertValue(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			Debug.Assert(parameters[1].NodeType == ExpressionType.Constant);
			state = state.WithSet(GetDictionaryIndexer(parameters[0], parameters[1]), parameters[2]);
			var version = state.TryFindAssignment(Expression.Field(parameters[0], "version"));
			if (version != null) state = state.WithSet(Expression.Field(parameters[0], "version"), Expression.Add(version, Expression.Constant(1)).Simplify());
			return state.ClearStack();
		}

		public static ExecutionState Dictionary_GetValue(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			Debug.Assert(parameters[1].NodeType == ExpressionType.Constant);
			var indexer = GetDictionaryIndexer(parameters[0], parameters[1]);
			var x = state.TryFindAssignment(indexer);
			if (x != null) return state.WithStack(new[] { x });

			throw new NotImplementedException();
		}

		public static ExecutionState Dictionary_ContainsKey(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			Debug.Assert(parameters[1].NodeType == ExpressionType.Constant);
			var indexer = GetDictionaryIndexer(parameters[0], parameters[1]);
			var x = state.TryFindAssignment(indexer);
			if (x != null) return state.WithStack(new[] { Expression.Constant(true) });
			else return state.WithStack(new[] { Expression.Constant(false) });
		}


		private static Expression GetDictionaryIndexer(Expression target, Expression index)
			=> Expression.MakeIndex(target, target.Type.GetProperty("Item"), new[] { index });

		public static ExecutionState Dictionary_TryGetValue(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			Debug.Assert(parameters[1].NodeType == ExpressionType.Constant);
			var indexer = GetDictionaryIndexer(parameters[0], parameters[1]);
			var result = (parameters[2].Resolve(state) as AddressOfExpression).Object;
			var x = state.TryFindAssignment(indexer);
			if (x != null)
			{
				return state.WithSet(result, x).WithStack(new[] { Expression.Constant(true) });
			}
			else return state.WithSet(result, Expression.Default(result.Type).Simplify()).WithStack(new[] { Expression.Constant(false) });
		}

		public static IList<KeyValuePair<Expression, Expression>> GetDictionaryEntries(ExecutionState state, Expression dictionary)
		{
			var array = new List<KeyValuePair<Expression, Expression>>();
			foreach (var s in state.GetSetExpressions())
			{
				if (s.Key.NodeType == ExpressionType.Index)
				{
					var target = ((IndexExpression)s.Key).Object;
					var indexers = ((IndexExpression)s.Key).Arguments;
					if (indexers.Count == 1 && ExpressionComparer.Instance.Equals(target, dictionary))
					{
						Debug.Assert(indexers[0].NodeType == ExpressionType.Constant);
						array.Add(new KeyValuePair<Expression, Expression>(indexers[0], s.Value));
					}
				}
			}
			return array;
		}
		public static ExecutionState Dictionary_GetEnumerator(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			var array = GetDictionaryEntries(state, parameters[0]);
			var elementType = typeof(KeyValuePair<,>).MakeGenericType(parameters[0].Type.GetGenericArguments());
			var ap = MyExpression.RootParameter(elementType.MakeArrayType(), SymbolicExecutor.NameParam("dictionaryEnumerableArray"), notNull: true, exactType: true);
			state = state.WithSet(ap, Expression.NewArrayBounds(elementType, Expression.Constant(array.Count)));
			var ctor = elementType.GetConstructor(elementType.GetGenericArguments());
			var evalArray = array.Select(k =>
			{
				state = exe.CallCtor(ctor, state.WithStack(new[] { k.Key, k.Value }));
				return state.Stack.Single();
			}).ToArray();
			state = state.WithSets(evalArray.Select((k, i) => new KeyValuePair<Expression, Expression>(Expression.Constant(i), k)));
			return exe.CallMethod(ap.Type.GetMethod("GetEnumerator"), state.WithStack(new[] { ap }), true);
		}

		public static ExecutionState Dictionary_Enumerator_MoveNext(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			var enumerator = parameters[0];
			var dict = state.TryFindAssignment(Expression.Field(enumerator, "dictionary"));
			Debug.Assert(dict != null);
			var index = (int)state.TryFindAssignment(Expression.Field(enumerator, "index")).GetConstantValue();
			state = state.WithSet(Expression.Field(enumerator, "index"), Expression.Constant(index + 1));
			var array = GetDictionaryEntries(state, dict);
			if (array.Count == index) return state.WithStack(new[] { Expression.Constant(false) });
			var elementType = typeof(KeyValuePair<,>).MakeGenericType(parameters[0].Type.GetGenericArguments());
			var ctor = elementType.GetConstructor(elementType.GetGenericArguments());
			state = exe.CallCtor(ctor, state.WithStack(new[] { array[index].Key, array[index].Value }));
			state = state.WithSet(Expression.Field(enumerator, "current"), state.Stack.Single());
			return state.WithStack(new[] { Expression.Constant(true) });
		}

		internal static ExecutionState Generic_GetEnumerator(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			if (typeof(IDictionary<,>).IsAssignableFrom(methodInfo.DeclaringType)) return Dictionary_GetEnumerator(exe, state, parameters, methodInfo);
			return null;
		}

		internal static ExecutionState Dictionary_Ctor(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			return state.ClearStack();
		}

		public static ExecutionState Object_GetType(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			var p = parameters[0].ProveType();
			if (p != null) return state.WithStack(new[] { Expression.Constant(p, typeof(Type)) });
			return null;
		}

		private readonly static MethodInfo string_Concat_method = typeof(string).GetMethod(nameof(String.Concat), BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string), typeof(string) }, null);
		public static ExecutionState String_Concat(SymbolicExecutor exe, ExecutionState state, IList<Expression> parameters, MethodBase methodInfo)
		{
			if (parameters.Count > 1)
			{
				parameters = parameters.Select(p =>
				{
					if (p.Type == typeof(string)) return p;
					var type = p.ProveType(state);
					if (type == typeof(string)) return Expression.Convert(p, typeof(string)).Simplify();
					else return Expression.Call(p, typeof(object).GetMethod(nameof(Object.ToString)));
				}).ToArray();
				var result = Expression.Add(parameters[0], parameters[1], string_Concat_method);
				for (int i = 2; i < parameters.Count; i++)
				{
					result = Expression.Add(result, parameters[i], string_Concat_method);
				}
				return state.ReplaceTopStack(parameters.Count, new[] { result });
			}
			else return state.ReplaceTopStack(parameters.Count, new[] { Expression.Call((MethodInfo)methodInfo, parameters) });
		}

		public static string String_Join_Strings_AI(string separator, string[] values, int from, int count)
		{
			bool first = true;
			var result = "";
			for (int i = 0; i < count; i++)
			{
				if (!first) result += separator;
				else first = false;
				result += values[i + from];
			}
			return result;
		}
		public static string String_Join_ObjectArray_AI(string separator, object[] values)
		{
			bool first = true;
			var result = "";
			for (int i = 0; i < values.Length; i++)
			{
				if (!first) result += separator;
				else first = false;
				result += values[i].ToString();
			}
			return result;
		}

		public static string String_Join_T_IEnumerable_AI<T>(string separator, IEnumerable<T> values)
		{
			bool first = true;
			var result = "";
			foreach (var str in values)
			{
				if (!first) result += separator;
				else first = false;
				result += str.ToString();
			}
			return result;
		}

		public static string String_Join_string_IEnumerable_AI(string separator, IEnumerable<string> values)
		{
			bool first = true;
			var result = "";
			foreach (var str in values)
			{
				if (!first) result += separator;
				else first = false;
				result += str;
			}
			return result;
		}
	}
}
