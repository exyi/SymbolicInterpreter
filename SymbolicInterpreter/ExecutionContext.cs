using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SymbolicInterpreter
{
    // type MethodExecutionContext { MethodInfo Method; MethodExecutionInfo; this Parent = null; noctor MethodExecState Stack ToDoStates = new() }
    public class MethodExecutionContext
    {
        public MethodInfo Method { get; }
        public MethodExecutionInfo MethodExecutionInfo { get; }
        public Stack<MethodExecState> ToDoStates { get; } = new Stack<MethodExecState>();
        public List<ExecutionState> ResultStates { get; } = new List<ExecutionState>();
        public MethodExecutionContext Parent { get; }

        public MethodExecutionContext(MethodInfo method, MethodExecutionInfo methodExecutionInfo, MethodExecutionContext parent)
        {
            this.Method = method;
            this.MethodExecutionInfo = methodExecutionInfo;
            this.Parent = parent;
        }
    }

    // struct type MethodExecCoreInfo { ExecutionState State; int EIP }

    public struct MethodExecState
    {
        public readonly ExecutionState MemState;
        public readonly int EIP;
        public readonly int MergeOn;
        public readonly ExecutionState MergeWith;

        public MethodExecState(ExecutionState state, int eip, int mergeOn, ExecutionState mergeWith)
        {
            this.MemState = state;
            this.EIP = eip;
            this.MergeOn = mergeOn;
            this.MergeWith = mergeWith;
        }
    }

    // struct type ExecCoreInfo extends MethodExecCoreInfo { MethodContext MethodContext; }
}
