using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public static class ExpressionUtils
    {
        public static bool IsConstant(this Expression expression) => expression.NodeType == ExpressionType.Constant;
        public static object GetConstantValue(this Expression expression) => ((ConstantExpression)expression).Value;

        public static bool IsOverrideOf(this MethodInfo method, MethodInfo baseMethod)
        {
            if (method == baseMethod) return true;
            if (!baseMethod.DeclaringType.IsAssignableFrom(method.DeclaringType)) return false;
            if (baseMethod.DeclaringType.IsInterface)
            {
                var ifcmap = method.DeclaringType.GetInterfaceMap(baseMethod.DeclaringType);
                var index = Array.IndexOf(ifcmap.InterfaceMethods, baseMethod);
                if (index >= 0)
                {
                    return ExpressionComparer.Equals(ifcmap.TargetMethods[index], method);
                }
            }
            else
            {
                var bm = method;
                var nbm = bm.GetBaseDefinition();
                while (nbm != bm)
                {
                    if (nbm == baseMethod)
                        return true;
                    bm = nbm;
                    nbm = bm.GetBaseDefinition();
                }
            }
            return false;
        }

        public static LambdaExpression IdentityLamda(Type type)
        {
            var p = Expression.Parameter(type);
            return Expression.Lambda(p, p);
        }

        public static Expression For(Expression length, Func<Expression, Expression> bodyFactory)
        {
            var i = Expression.Variable(typeof(int), "i");
            return Expression.Block(new[] { i },
                Expression.Assign(i, Expression.Constant(0)),
                While(Expression.LessThan(i, length),
                    Expression.Block(
                        bodyFactory(i),
                        Expression.PostIncrementAssign(i)
                    )
                )
            );
        }

        public static Expression Foreach(Expression enumerable, Func<Expression, Expression> bodyFactory)
        {
            // use for(int i = 0; i < a.Length; i++) ... for Arrays
            if (enumerable.Type.IsArray)
            {
                return For(Expression.ArrayLength(enumerable), i => bodyFactory(Expression.ArrayIndex(enumerable, i)));
            }
            else
            {
                var getEnumMethod = Expression.Call(enumerable, "GetEnumerator", Type.EmptyTypes);
                var enumerator = Expression.Variable(getEnumMethod.Type, "enumerator");
                var body = new List<Expression>();
                body.Add(Expression.Assign(enumerator, getEnumMethod));
                body.Add(Expression.TryFinally(While(
                    condition: Expression.Call(enumerator, nameof(IEnumerator<int>.MoveNext), Type.EmptyTypes),
                    body: bodyFactory(Expression.Property(enumerator, nameof(IEnumerator<int>.Current)))
                ),
                    Expression.Call(enumerator, "Dispose", Type.EmptyTypes)
                ));
                return Expression.Block(new[] { enumerator }, body);
            }
        }
        public static Expression While(Expression conditionBody)
        {
            var brkLabel = Expression.Label();
            return Expression.Loop(
                Expression.IfThen(Expression.Not(conditionBody), Expression.Goto(brkLabel)), brkLabel);
        }

        public static Expression While(Expression condition, Expression body)
        {
            var brkLabel = Expression.Label();
            return Expression.Loop(
                Expression.IfThenElse(condition, body, Expression.Goto(brkLabel)), brkLabel);
        }

        /// <summary>
        /// ensures that parameter used in body is read and written only once
        /// </summary>
        /// <param name="type">Type of temporary variable</param>
        /// <param name="getter">The property getter</param>
        /// <param name="setter">the property setter expression, if null, assignments are ignored</param>
        /// <param name="bodyFactory">body of the expression</param>
        public static Expression OneUse(Type type, Expression getter, Func<Expression, Expression> setter, Func<Expression, Expression> bodyFactory)
        {
            var var = Expression.Variable(type, "tmp");
            var body = bodyFactory(var);
            var findUsage = new FindParameterUsageVisitor();
            findUsage.Query = var;
            findUsage.Visit(body);
            var replacer = new ExpressionReplaceVisitor(EqualityComparer<Expression>.Default);
            if (findUsage.ReadCount == 1)
            {
                replacer.Replace.Add(var, new Lazy<Expression>(() => getter));
            }
            if (findUsage.Assignments.Count == 1 && setter != null)
            {
                replacer.Replace.Add(findUsage.Assignments[0], new Lazy<Expression>(() => setter(findUsage.Assignments[0].Right)));
            }
            var block = new List<Expression>();
            if (findUsage.ReadCount > 1)
            {
                block.Add(Expression.Assign(var, getter));
            }
            block.Add(replacer.Visit(body));
            if (findUsage.Assignments.Count > 1 && setter != null)
            {
                block.Add(setter(var));
            }
            if (block.Count == 1 && (findUsage.Assignments.Count == 0 || setter != null)) return block[0];
            else return Expression.Block(new[] { var }, block);
        }

        public static bool IsNoCost(this Expression expression)
        {
            if (expression is ParameterExpression) return true;
            return false;
        }


        public static Expression Replace(this Expression ex, Expression replace, Expression with)
        {
            var rv = new ExpressionReplaceVisitor(EqualityComparer<Expression>.Default);
            rv.Replace.Add(replace, new Lazy<Expression>(() => with));
            return rv.Visit(ex);
        }

        public static Expression ReplaceParams(this LambdaExpression lambda, params Expression[] parameters)
        {
            var rv = new ExpressionReplaceVisitor(EqualityComparer<Expression>.Default);
            Debug.Assert(lambda.Parameters.Count == parameters.Length);
            for (int i = 0; i < lambda.Parameters.Count; i++)
            {
                var p = parameters[i];
                Debug.Assert(lambda.Parameters[i].Type.IsAssignableFrom(parameters[i].Type));
                rv.Replace.Add(lambda.Parameters[i], new Lazy<Expression>(() => p));
            }
            return rv.Visit(lambda.Body);
        }

        public static Expression ReplaceParamsOneUse(this LambdaExpression lamda, params Expression[] parameters)
        {
            var params2 = new Expression[parameters.Length];
            Func<Expression> body = () => lamda.ReplaceParams(params2);
            int i = 0;
            foreach (var p in parameters)
            {
                var i2 = i;
                if (!p.IsNoCost())
                {
                    var body2 = body;
                    body = () => OneUse(p.Type, p, null, tp =>
                    {
                        params2[i2] = tp;
                        return body2();
                    });
                }
                else params2[i] = p;
                i++;
            }
            return body();
        }

        public static Expression CtorCollection(Type collectionType, Expression enumerable)
        {
            if (enumerable.Type.IsAssignableFrom(collectionType)) return Expression.Convert(enumerable, collectionType);
            if (collectionType.IsArray) return Expression.Call(typeof(Enumerable).GetMethod("ToArray"), enumerable);
            var collectionCtor = (from c in collectionType.GetConstructors(BindingFlags.Public)
                                  let p = c.GetParameters()
                                  where p.Length == 1
                                  where p[0].ParameterType.IsAssignableFrom(enumerable.Type)
                                  select c).FirstOrDefault();
            if (collectionCtor != null)
            {
                return Expression.New(collectionCtor, enumerable);
            }
            // TODO: support some most common interfaces
            throw new NotSupportedException();
        }
        public static Expression WalkTree(this Expression expr, Func<Expression, Expression> nodeAction, bool rewalkOnReplace= false)
        {
            return new GenericExpressionWalkVisitor() { VisitCallback = nodeAction, RewalkOnReplace = rewalkOnReplace }.Visit(expr);
        }

        public static void WalkTree(this Expression expr, Action<Expression> nodeAction)
        {
            new GenericExpressionWalkVisitor() { VisitCallback = e => { nodeAction(e); return null; } }.Visit(expr);
        }

        public static ISet<Expression> DoesNotContain(this Expression a, IEnumerable<Expression> b, IEqualityComparer<Expression> comparer = null)
        {
            var hashSet = new HashSet<Expression>(b, comparer ?? ExpressionComparer.Instance);
            a.WalkTree(e => hashSet.Remove(e));
            return hashSet;
        }

        public static Expression FailWith<TException>(string message)
        {
            var ctor = typeof(TException).GetConstructor(new Type[] { typeof(string) });
            return Expression.Throw(Expression.New(ctor, Expression.Constant(message)));
        }

        public static Expression FailWith(string message)
            => FailWith<Exception>(message);

        public static IEnumerable<ParameterExpression> GetUsedParameters(this Expression expr)
        {
            var v = new ParameterFindingVisitor();
            v.Visit(expr);
            return v.Parameters;
        }

        public static bool Contains(this Expression expr, Expression query)
        {
            var ctv = new ExpressionContainsVisitor() { Query = query };
            ctv.Visit(expr);
            return ctv.Result;
        }

        public static string GetDebugView(this Expression expr)
        {
            var prop = typeof(Expression).GetProperty("DebugView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (string)prop.GetValue(PrepareForDebugViewVisitor.Instance.Visit(expr));
        }

        class PrepareForDebugViewVisitor : ExpressionVisitor
        {
            public static readonly PrepareForDebugViewVisitor Instance = new PrepareForDebugViewVisitor();

            protected override Expression VisitMyParameter(MyParameterExpression parameter)
            {
                return Expression.Parameter(parameter.Type, (parameter.IsRoot ? "%" : (parameter.IsMutable ? "" : "!")) + parameter.Name);
            }
        }

        public static Expression WrapWithLambda(this Expression expr, LambdaExpression lambda)
        {
            if (lambda.Parameters.Count != 1) throw new ArgumentException();
            return lambda.ReplaceParamsOneUse(expr);
        }
    }

    /// <summary>
    /// sets to <see cref="Result"/> if visited expression contains <see cref="Query"/> 
    /// </summary>
    class ExpressionContainsVisitor : ExpressionVisitor
    {
        public bool Result { get; set; }
        public Expression Query { get; set; }

        public override Expression Visit(Expression node)
        {
            if (ExpressionComparer.Instance.Equals(node, Query))
            {
                Result = true;
            }

            if (Result) return node;
            return base.Visit(node);
        }
    }

    class ParameterReplaceVisitor : ExpressionVisitor
    {
        public Dictionary<string, Expression> NamedParams { get; } = new Dictionary<string, Expression>();
        public Dictionary<ParameterExpression, Expression> Parameters { get; } = new Dictionary<ParameterExpression, Expression>();
        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (Parameters.ContainsKey(node)) return Parameters[node];
            if (!string.IsNullOrEmpty(node.Name) && NamedParams.ContainsKey(node.Name)) return NamedParams[node.Name];
            else return base.VisitParameter(node);
        }
    }

    public class ExpressionReplaceVisitor : ExpressionVisitor
    {
        protected IEqualityComparer<Expression> comparer;
        public Dictionary<Expression, Lazy<Expression>> Replace { get; }
        public ExpressionReplaceVisitor() : this(ExpressionComparer.Instance)
        { }
        public ExpressionReplaceVisitor(IEqualityComparer<Expression> comparer)
        {
            this.comparer = comparer;
            Replace = new Dictionary<Expression, Lazy<Expression>>(comparer);
        }
        public override Expression Visit(Expression node)
        {
            if (node == null) return null;
            Lazy<Expression> replaced;
            if (Replace.TryGetValue(node, out replaced)) return replaced.Value;
            return base.Visit(node);
        }
    }

    class ParameterFindingVisitor : ExpressionVisitor
    {
        public HashSet<ParameterExpression> Parameters { get; } = new HashSet<ParameterExpression>(ExpressionComparer.Instance);

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Parameters.Add(node);
            return base.VisitParameter(node);
        }
    }

    class FindTopPropertiesVisitor : ExpressionVisitor
    {
        public Expression TheExpression { get; set; }
        public IEqualityComparer<Expression> Comparer { get; private set; }
        public FindTopPropertiesVisitor(IEqualityComparer<Expression> comparer)
        {
            Comparer = comparer;
        }
        public FindTopPropertiesVisitor() : this(new ExpressionComparer()) { }

        bool foundTopUsage = false;
        public bool FoundTopUsage => foundTopUsage;
        public override Expression Visit(Expression node)
        {
            if (node == TheExpression || Comparer.Equals(node, TheExpression)) foundTopUsage = true;
            return base.Visit(node);
        }
        List<Expression> foundMembers = new List<Expression>();
        public IReadOnlyList<Expression> FoundList => foundMembers.AsReadOnly();
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == TheExpression || Comparer.Equals(node.Expression, TheExpression))
                foundMembers.Add(node);
            return base.VisitMember(node);
        }

        public static bool UsesOnlyProperty(Expression expr, Expression property, IEqualityComparer<Expression> comparer)
        {
            var v = new FindTopPropertiesVisitor(comparer);
            v.Visit(expr);
            return !v.FoundTopUsage && v.FoundList.All(m => m == property || comparer.Equals(m, property));
        }
    }
    public class FindParameterUsageVisitor : ExpressionVisitor
    {
        public ParameterExpression Query { get; set; }
        public int ReadCount { get; set; }
        public List<BinaryExpression> Assignments { get; set; } = new List<BinaryExpression>();

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.Assign && node.Left == Query)
            {
                Assignments.Add(node);
                return node.Update(node.Left, VisitAndConvert(node.Conversion, "VisitBinary"), Visit(node.Right));
            }
            else return base.VisitBinary(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == Query) ReadCount++;
            return base.VisitParameter(node);
        }
    }
    public class GenericExpressionWalkVisitor : ExpressionVisitor
    {
        public Func<Expression, Expression> VisitCallback { get; set; }
        public bool RewalkOnReplace { get; set; }

        public GenericExpressionWalkVisitor(Func<Expression, Expression> callback = null)
        {
            VisitCallback = callback;
        }

        public override Expression Visit(Expression node)
        {
            if (node == null) return null;
            var newNode = VisitCallback?.Invoke(node);
            if (RewalkOnReplace || newNode == null) return base.Visit(newNode ?? node);
            return newNode;
        }
    }
}
