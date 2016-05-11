using System;
using System.Collections.Generic;
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
            if (!baseMethod.DeclaringType.IsAssignableFrom(method.DeclaringType)) return false;
            if (baseMethod.DeclaringType.IsInterface)
            {
                var ifcmap = method.DeclaringType.GetInterfaceMap(baseMethod.DeclaringType);
                var index = Array.IndexOf(ifcmap.TargetMethods, method);
                if (index >= 0)
                {
                    return ifcmap.InterfaceMethods[index] == baseMethod;
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

    class ExpressionReplaceVisitor : ExpressionVisitor
    {
        protected IEqualityComparer<Expression> comparer;
        public Dictionary<Expression, Lazy<Expression>> Replace { get; }
        public ExpressionReplaceVisitor() : this(new ExpressionComparer(true))
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

        public GenericExpressionWalkVisitor(Func<Expression, Expression> callback = null)
        {
            VisitCallback = callback;
        }

        public override Expression Visit(Expression node)
        {
            return VisitCallback?.Invoke(node) ?? base.Visit(node);
        }
    }
}
