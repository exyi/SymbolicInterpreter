using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    //public static class DependencyAnalyzer
    //{
    //    public DependencyGraphNode AnalyzeForExpression(ExecutionState state, Expression expr)
    //    {

    //    }

    //    class Visitor: ExpressionVisitor
    //    {
            
    //    }
    //}

    public class DependencyGraphNode
    {
        public Expression Expression { get; set; }
        public DependencyGraphNode[] DependentOn { get; set; }

        protected Dictionary<IProperty, object> properties;
        public bool TryGet<T>(Property<T> prop, out T value)
        {
            object objValue;
            if (properties != null && properties.TryGetValue(prop, out objValue))
            {
                value = (T)objValue;
                return true;
            }
            else
            {
                value = prop.DefaultValue;
                return false;
            }
        }

        public void Set<T>(Property<T> prop, T value)
        {
            if (properties == null) properties = new Dictionary<IProperty, object>();
            properties[prop] = value;
        }

        public interface IProperty
        {
            object DefaultValue { get; }
            Type Type { get; }
        }
        public class Property<T> : IProperty
        {
            public Property(T defaultValue = default(T))
            {
                this.DefaultValue = defaultValue;
            }

            public T DefaultValue { get; }
            object IProperty.DefaultValue => DefaultValue;
            Type IProperty.Type => typeof(T);
        }
        public static class Properties
        {
            public static readonly Property<int> SideEffectIndexProperty = new Property<int>();
        }
    }
}
