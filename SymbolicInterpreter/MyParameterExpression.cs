using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public class MyParameterExpression : Expression
    {
        public override Type Type { get; }
        public string Name { get; }
        public object ObjId { get; }

        public override ExpressionType NodeType
        {
            get
            {
                var sc = new StackTrace().GetFrames().Take(2);
                if (sc.Any(m => m.GetMethod().Name == "RequiresCanWrite")) return ExpressionType.Parameter;

                return ExpressionType.Extension;
            }
        }
        public bool IsRoot { get; }
        public bool IsMutable { get; }
        public bool ExactType { get; }
        public bool NotNull { get; }

        public MyParameterExpression(Type type, string name, object objId = null, bool root = false, bool mutable = true, bool notNull = false, bool exactType = false)
        {
            Type = type;
            Name = name;
            ObjId = objId ?? this;
            IsMutable = mutable;
            IsRoot = root;
            ExactType = exactType;
            NotNull = notNull;
        }

        public override string ToString()
        {
            return $"{Name}";
        }
    }
}
