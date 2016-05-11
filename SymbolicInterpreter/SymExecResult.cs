using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public class SymExecResult
    {
        public Expression Return { get; private set; }
        public Expression Branch { get; private set; }
        public Sigil.Label BranchTo { get; private set; }

        public bool IsSet => Return != null || BranchTo != null || Rethrow || Throw != null;

        public bool Rethrow { get; private set; }
        public Expression Throw { get; private set; }

        public void SetBranching(Sigil.Label branchTo, Expression branchIf = null)
        {
            if (IsSet) throw new InvalidOperationException();
            BranchTo = branchTo;
            Branch = branchIf ?? Expression.Constant(true);
        }

        public void SetReturn(Expression expression = null)
        {
            if (IsSet) throw new InvalidOperationException();
            Return = expression ?? Expression.Default(typeof(void));
        }

        public void SetRethrow()
        {
            if (IsSet) throw new InvalidOperationException();
            Rethrow = true;
        }

        public void SetThrow(Expression expr)
        {
            if (IsSet) throw new InvalidOperationException();
            Throw = expr;
        }
    }
}
