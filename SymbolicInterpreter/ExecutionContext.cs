using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    public class ExecutionContext
    {
        //public Stack<ToExectute> ToDoList { get; }
        public MethodInfo Method { get; }
        public MethodContext MethodContext { get; }
        public ExecutionContext Parent { get; }
    }

    // struct type MethodExecCoreInfo { ExecutionState State; int EIP }

    public struct MethodExecCoreInfo
    {
        public readonly ExecutionState State;
        public readonly int EIP;

        public MethodExecCoreInfo(ExecutionState state, int eip)
        {
            this.State = state;
            this.EIP = eip;
        }
    }

    // struct type ExecCoreInfo extends MethodExecCoreInfo { MethodContext MethodContext; }
}
