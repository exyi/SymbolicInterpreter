using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public static class MyExpression
    {
        public static MyParameterExpression Parameter(Type type, string name = "__", bool mutable = true, bool root = false, bool notNull = false, bool exactType = false)
        {
            if (root) Debug.Assert(!mutable);
            return new MyParameterExpression(type, name, root: root, mutable: mutable, notNull: notNull, exactType: exactType);
        }
        public static MyParameterExpression RootParameter(Type type, string name = "__", bool notNull = false, bool exactType = false)
        {
            return new MyParameterExpression(type, name, root: true, mutable: true, notNull: notNull, exactType: exactType);
        }

    }
}
