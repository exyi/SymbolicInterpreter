using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SymbolicInterpreter.StackConversion;

namespace SymbolicInterpreter
{
    public class SymbolicExecutor
    {
        private static ConcurrentDictionary<MethodBase, DisassemblyResult> disassemblyCache = new ConcurrentDictionary<MethodBase, DisassemblyResult>();
        public static DisassemblyResult Disassembly(MethodBase method)
        {
            return disassemblyCache.GetOrAdd(method, m =>
            {
                var target = m.IsStatic ? null : m.DeclaringType;

                var op = Sigil.Disassembler<Action>.Disassemble(m, target);
                return TransformDisassemblyResults((m as MethodInfo)?.ReturnType ?? typeof(void), op);
            });
        }

        private static DisassemblyResult TransformDisassemblyResults<T>(Type returnType, Sigil.DisassembledOperations<T> op)
        {
            var instructions = op.Select(s => new MethodInstruction(
                s.IsCatchBlockEnd ? InstructionType.CatchBlockEnd :
                s.IsCatchBlockStart ? InstructionType.CatchBlockStart :
                s.IsExceptionBlockEnd ? InstructionType.ExceptionBlockEnd :
                s.IsExceptionBlockStart ? InstructionType.ExceptionBlockStart :
                s.IsFinallyBlockEnd ? InstructionType.FinallyBlockEnd :
                s.IsFinallyBlockStart ? InstructionType.FinallyBlockStart :
                s.IsFaultBlockStart ? InstructionType.FaultBlockStart :
                s.IsFaultBlockEnd ? InstructionType.FaultBlockEnd :
                s.IsMarkLabel ? InstructionType.MarkLabel :
                s.IsOpCode ? InstructionType.OpCode : InstructionType.None,
                labelName: s.LabelName,
                opCode: s.OpCode,
                parameters: s.Parameters.ToArray())
            ).ToArray();
            return new DisassemblyResult(instructions, op.Labels.ToArray(), op.Locals.ToArray(), op.Parameters.ToArray(), returnType);
        }

        private static int pcnt = 0;

        protected MethodExecutionInfo CreateMethodContext(DisassemblyResult disassembly)
            => new MethodExecutionInfo(disassembly,
                disassembly.Locals.Select(l => MyExpression.Parameter(l.LocalType, $"local_{l.Name}__{Interlocked.Increment(ref pcnt)}")).ToArray(),
                disassembly.Parameters.Select(p => MyExpression.Parameter(p.ParameterType, $"param_{p.Name}__{Interlocked.Increment(ref pcnt)}")).ToArray());

        public ExecutionState Execute(ExecutionState state, DisassemblyResult result, IList<Expression> parameters)
        {
            var mc = CreateMethodContext(result);
            var opCodes = result.Instructions
                .Where(i => i.IsOpCode)
                .Select(i => i.OpCode)
                .Distinct()
                .ToArray();

            state = state.ClearStack().WithSets(mc.Parameters.Zip(parameters, (p, val) => new KeyValuePair<Expression, Expression>(p, val)));

            return ExecCore(mc, state, 0);
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private ExecutionState ExecCore(MethodExecutionInfo mc, ExecutionState state, int eip)
        {
            var excBlockStack = new Stack<Tuple<MethodInstruction, ExecutionState>>();
            ExecutionState currentExceptionBlock = null;
            var stack = new Stack<Expression>(state.Stack);
            var instructions = mc.Disassembly.Instructions;
            var prefixes = new PrefixTracker();
            var results = new SymExecResult();
            while (true)
            {
                var instr = instructions[eip];
                Debug.Assert(currentExceptionBlock == null || instr.IsFinallyBlockStart || instr.IsCatchBlockStart);
                if (instr.IsOpCode)
                {
                    var op = instr.OpCode;

                    if (op == OpCodes.Add)
                    {
                        var a = ConvertToSigned(stack.Pop());
                        var b = ConvertToSigned(stack.Pop());
                        stack.Push(Expression.AddChecked(a, b));
                    }

                    else if (op == OpCodes.Add_Ovf)
                    {
                        var a = ConvertToSigned(stack.Pop());
                        var b = ConvertToSigned(stack.Pop());
                        stack.Push(Expression.Add(a, b));
                    }

                    else if (op == OpCodes.Add_Ovf_Un)
                    {
                        var a = ConvertToUnsigned(stack.Pop());
                        var b = ConvertToUnsigned(stack.Pop());
                        stack.Push(Expression.AddChecked(a, b));
                    }

                    else if (op == OpCodes.And)
                    {
                        var a = ConvertToBool(stack.Pop());
                        var b = ConvertToBool(stack.Pop());
                        stack.Push(Expression.And(a, b));
                    }

                    else if (op == OpCodes.Arglist)
                    {
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Beq || op == OpCodes.Beq_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = stack.Pop();
                        var a = stack.Pop();
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.Equal(a, b));
                        break;
                    }

                    else if (op == OpCodes.Bge || op == OpCodes.Bge_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = ConvertToUnsigned(stack.Pop());
                        var a = ConvertToUnsigned(stack.Pop());
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.Not(Expression.LessThan(a, b)));
                        break;
                    }

                    else if (op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = ConvertToSigned(stack.Pop());
                        var a = ConvertToSigned(stack.Pop());
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.Not(Expression.LessThan(a, b)));
                        break;
                    }

                    else if (op == OpCodes.Bgt || op == OpCodes.Bgt_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = ConvertToSigned(stack.Pop());
                        var a = ConvertToSigned(stack.Pop());
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.GreaterThan(a, b));
                        break;
                    }

                    else if (op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = ConvertToUnsigned(stack.Pop());
                        var a = ConvertToUnsigned(stack.Pop());
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.GreaterThan(a, b));
                        break;
                    }

                    else if (op == OpCodes.Ble || op == OpCodes.Ble_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = ConvertToSigned(stack.Pop());
                        var a = ConvertToSigned(stack.Pop());
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.Not(Expression.GreaterThan(a, b)));
                        break;
                    }

                    else if (op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = ConvertToUnsigned(stack.Pop());
                        var a = ConvertToUnsigned(stack.Pop());
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.Not(Expression.GreaterThan(a, b)));
                        break;
                    }

                    else if (op == OpCodes.Blt || op == OpCodes.Blt_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = ConvertToSigned(stack.Pop());
                        var a = ConvertToSigned(stack.Pop());
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.LessThan(a, b));
                    }

                    else if (op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = ConvertToUnsigned(stack.Pop());
                        var a = ConvertToUnsigned(stack.Pop());
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.LessThan(a, b));
                    }

                    else if (op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S)
                    {
                        var label = instr.GetParameterLabel();
                        var b = stack.Pop();
                        var a = stack.Pop();
                        OperatorConvert(ref a, ref b);
                        results.SetBranching(label, Expression.Not(Expression.Equal(a, b)));
                        break;
                    }

                    else if (op == OpCodes.Box)
                    {
                        var valType = instr.GetParameterType();
                        var p = ImplicitConvertTo(stack.Pop(), valType);
                        stack.Push(Expression.Convert(p, typeof(object)));
                    }

                    else if (op == OpCodes.Br || op == OpCodes.Br_S)
                    {
                        var label = instr.GetParameterLabel();
                        results.SetBranching(label);
                        break;
                    }

                    else if (op == OpCodes.Break)
                    {
                        // can probably ignore ??
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Brfalse || op == OpCodes.Brfalse_S)
                    {
                        var label = instr.GetParameterLabel();
                        var p = ConvertToBool(stack.Pop());
                        results.SetBranching(label, Expression.Not(p));
                        break;
                    }

                    else if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S)
                    {
                        var label = instr.GetParameterLabel();
                        var p = ConvertToBool(stack.Pop());
                        results.SetBranching(label, p);
                        break;
                    }

                    else if (op == OpCodes.Call || op == OpCodes.Callvirt)
                    {
                        Type constrained;
                        var method = instr.GetParameterMethod(out constrained);
                        state = CallMethod(method, state.WithStack(stack), op == OpCodes.Callvirt, constrained);
                        stack = new Stack<Expression>(state.Stack);
                    }

                    else if (op == OpCodes.Calli)
                    {
                        throw new NotImplementedException("Calli is not supported in Sigil.Disassembler");
                    }

                    else if (op == OpCodes.Castclass)
                    {
                        var type = instr.GetParameterType();
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(p, type));
                    }

                    else if (op == OpCodes.Ceq)
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        OperatorConvert(ref a, ref b);
                        stack.Push(Expression.Equal(a, b));
                    }

                    else if (op == OpCodes.Cgt)
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        if (b.NodeType == ExpressionType.Constant && !a.Type.IsValueType && b.GetConstantValue() == null)
                            stack.Push(Expression.Not(Expression.ReferenceEqual(a, b)));
                        else stack.Push(Expression.GreaterThan(ConvertToSigned(a), ConvertToSigned(b)));
                    }

                    else if (op == OpCodes.Cgt_Un)
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        if (b.NodeType == ExpressionType.Constant && !a.Type.IsValueType && b.GetConstantValue() == null)
                            stack.Push(Expression.Not(Expression.ReferenceEqual(a, b)));
                        else stack.Push(Expression.GreaterThan(ConvertToUnsigned(a), ConvertToUnsigned(b)));
                    }

                    else if (op == OpCodes.Ckfinite)
                    {
                        // TODO: impl
                    }

                    else if (op == OpCodes.Clt)
                    {
                        var b = ConvertToSigned(stack.Pop());
                        var a = ConvertToSigned(stack.Pop());
                        stack.Push(Expression.LessThan(a, b));
                    }

                    else if (op == OpCodes.Clt_Un)
                    {
                        var b = ConvertToUnsigned(stack.Pop());
                        var a = ConvertToUnsigned(stack.Pop());
                        stack.Push(Expression.LessThan(a, b));
                    }

                    else if (op == OpCodes.Constrained)
                    {
                        var type = instr.GetParameterType();
                        prefixes.SetConstrained(type);
                    }

                    else if (op == OpCodes.Conv_I)
                    {
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Conv_I1)
                    {
                        // TODO: overflow?
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToSigned(p), typeof(byte)));
                    }

                    else if (op == OpCodes.Conv_I2)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToSigned(p), typeof(short)));
                    }

                    else if (op == OpCodes.Conv_I4)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToSigned(p), typeof(int)));
                    }

                    else if (op == OpCodes.Conv_I8)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToSigned(p), typeof(long)));
                    }

                    else if (op == OpCodes.Conv_Ovf_I)
                    {
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Conv_Ovf_I_Un)
                    {
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Conv_Ovf_I1)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToSigned(p), typeof(byte)));
                    }

                    else if (op == OpCodes.Conv_Ovf_I1_Un)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToUnsigned(p), typeof(byte)));
                    }

                    else if (op == OpCodes.Conv_Ovf_I2)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToSigned(p), typeof(short)));
                    }

                    else if (op == OpCodes.Conv_Ovf_I2_Un)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToUnsigned(p), typeof(short)));
                    }

                    else if (op == OpCodes.Conv_Ovf_I4)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToSigned(p), typeof(int)));
                    }

                    else if (op == OpCodes.Conv_Ovf_I4_Un)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToUnsigned(p), typeof(int)));
                    }

                    else if (op == OpCodes.Conv_Ovf_I8)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToSigned(p), typeof(long)));
                    }

                    else if (op == OpCodes.Conv_Ovf_I8_Un)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Convert(ConvertToUnsigned(p), typeof(long)));
                    }

                    else if (op == OpCodes.Conv_Ovf_U)
                    {
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Conv_Ovf_U_Un)
                    {
                        throw new NotSupportedException();
                    }

                    //else if (op == OpCodes.Conv_Ovf_U1)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.ConvertOverflow(typeof(byte))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_Ovf_U1_Un)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.UnsignedConvertOverflow(typeof(byte))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_Ovf_U2)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.ConvertOverflow(typeof(ushort))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_Ovf_U2_Un)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.UnsignedConvertOverflow(typeof(ushort))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_Ovf_U4)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.ConvertOverflow(typeof(uint))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_Ovf_U4_Un)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.UnsignedConvertOverflow(typeof(uint))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_Ovf_U8)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.ConvertOverflow(typeof(ulong))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_Ovf_U8_Un)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.UnsignedConvertOverflow(typeof(ulong))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_R_Un)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.UnsignedConvertToFloat()
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_R4)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.Convert(typeof(float))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_R8)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.Convert(typeof(double))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_U)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.Convert(typeof(UIntPtr))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_U1)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.Convert(typeof(byte))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_U2)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.Convert(typeof(ushort))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_U4)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.Convert(typeof(uint))
                    //        };
                    //}

                    //else if (op == OpCodes.Conv_U8)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[0],
                    //            Replay = emit => emit.Convert(typeof(ulong))
                    //        };
                    //}

                    else if (op == OpCodes.Cpblk)
                    {
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Cpobj)
                    {
                        var type = instr.GetParameterType();
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Div)
                    {
                        var a = stack.Pop();
                        var b = stack.Pop();
                        stack.Push(Expression.Divide(a, b));
                    }

                    else if (op == OpCodes.Div_Un)
                    {
                        var a = stack.Pop();
                        var b = stack.Pop();
                        stack.Push(Expression.Divide(ConvertToUnsigned(a), ConvertToUnsigned(b)));
                    }

                    else if (op == OpCodes.Dup)
                    {
                        stack.Push(stack.Peek());
                    }

                    else if (op == OpCodes.Endfilter)
                    {
                        throw new InvalidOperationException("Sigil does not support fault blocks, or the Endfilter opcode");
                    }

                    else if (op == OpCodes.Endfinally)
                    {
                        // Endfinally isn't emitted directly by ILGenerator or Sigil; it's implicit in EndFinallyBlock() calls
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Initblk)
                    {
                        var isVolatile = (bool)instr.Parameters[0];
                        int? unaligned = (int?)instr.Parameters[1];
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Initobj)
                    {
                        var type = instr.GetParameterType();
                        var target = stack.Pop();
                        if (target is AddressOfExpression)
                        {
                            var expr = ((AddressOfExpression)target).Object;
                            state = SetVar(state.WithStack(stack), expr, Expression.Default(type));
                            stack = new Stack<Expression>(state.Stack);
                        }
                        else throw new NotImplementedException();
                    }

                    else if (op == OpCodes.Isinst)
                    {
                        var type = instr.GetParameterType();
                        var p = stack.Pop();
                        if (p.Type.IsValueType) throw new NotSupportedException();
                        stack.Push(Expression.TypeAs(p, type));
                    }

                    else if (op == OpCodes.Jmp)
                    {
                        var mtd = instr.GetParameterMethod();
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_0 || op == OpCodes.Ldarg_1 || op == OpCodes.Ldarg_2 || op == OpCodes.Ldarg_3 || op == OpCodes.Ldarg_S)
                    {
                        var index = instr.GetParameterIndex();
                        stack.Push(mc.Parameters[index]);
                    }

                    else if (op == OpCodes.Ldarga || op == OpCodes.Ldarga_S)
                    {
                        var index = instr.GetParameterIndex();
                        stack.Push(new AddressOfExpression(mc.Parameters[index]));
                    }

                    else if (op == OpCodes.Ldc_I4 ||
                        op == OpCodes.Ldc_I4_0 || op == OpCodes.Ldc_I4_1 || op == OpCodes.Ldc_I4_2 || op == OpCodes.Ldc_I4_3 || op == OpCodes.Ldc_I4_4 || op == OpCodes.Ldc_I4_5 ||
                        op == OpCodes.Ldc_I4_6 || op == OpCodes.Ldc_I4_7 || op == OpCodes.Ldc_I4_8 || op == OpCodes.Ldc_I4_M1 || op == OpCodes.Ldc_I4_S)
                    {
                        var c = instr.GetParameter<int>();
                        stack.Push(Expression.Constant(c));
                    }

                    else if (op == OpCodes.Ldc_I8)
                    {
                        var c = instr.GetParameter<long>();
                        stack.Push(Expression.Constant(c));
                    }

                    else if (op == OpCodes.Ldc_R4)
                    {
                        var c = instr.GetParameter<float>();
                        stack.Push(Expression.Constant(c));
                    }

                    else if (op == OpCodes.Ldc_R8)
                    {
                        var c = instr.GetParameter<double>();
                        stack.Push(Expression.Constant(c));
                    }

                    else if (op == OpCodes.Ldelem || op == OpCodes.Ldelem_I || op == OpCodes.Ldelem_I1 || op == OpCodes.Ldelem_I2 || op == OpCodes.Ldelem_I4 || op == OpCodes.Ldelem_I8 ||
                        op == OpCodes.Ldelem_R4 || op == OpCodes.Ldelem_R8 || op == OpCodes.Ldelem_U1 || op == OpCodes.Ldelem_U2 || op == OpCodes.Ldelem_U4 || op == OpCodes.Ldelem_Ref)
                    {
                        // TODO: correct the types
                        var type = instr.GetParameterType();
                        var index = stack.Pop();
                        var array = stack.Pop();
                        stack.Push(Expression.ArrayIndex(array, index));
                    }

                    else if (op == OpCodes.Ldelema)
                    {
                        var type = instr.GetParameterType();
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Ldfld)
                    {
                        var fld = instr.GetParameterField();
                        var isVolatile = prefixes.HasVolatile;
                        int? unalgined = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                        var target = stack.Pop();
                        stack.Push(Expression.Field(target, fld));
                    }

                    else if (op == OpCodes.Ldflda)
                    {
                        var fld = instr.GetParameterField();
                        var target = stack.Pop();
                        stack.Push(new AddressOfExpression(Expression.Field(target, fld)));
                    }

                    else if (op == OpCodes.Ldftn)
                    {
                        // load function pointer
                        var mtd = instr.GetParameterMethod().CastTo<MethodInfo>();
                        stack.Push(new FunctionPointerExpression(mtd));
                    }

                    //if (op == OpCodes.Ldind_I)
                    //{
                    //    var type = typeof(IntPtr);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_I1)
                    //{
                    //    var type = typeof(sbyte);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_I2)
                    //{
                    //    var type = typeof(short);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_I4)
                    //{
                    //    var type = typeof(int);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_I8)
                    //{
                    //    var type = typeof(long);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_R4)
                    //{
                    //    var type = typeof(float);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_R8)
                    //{
                    //    var type = typeof(double);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_Ref)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = null,
                    //            Replay = emit => emit.LoadIndirect<WildcardType>(),

                    //            Prefixes = prefixes.Clone()
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_U1)
                    //{
                    //    var type = typeof(byte);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_U2)
                    //{
                    //    var type = typeof(ushort);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Ldind_U4)
                    //{
                    //    var type = typeof(uint);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.LoadIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    else if (op == OpCodes.Ldlen)
                    {
                        var array = stack.Pop();
                        stack.Push(Expression.ArrayLength(array));
                    }

                    else if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_0 || op == OpCodes.Ldloc_1 || op == OpCodes.Ldloc_2 || op == OpCodes.Ldloc_3 || op == OpCodes.Ldloc_S)
                    {
                        var index = instr.GetParameterIndex();
                        stack.Push(mc.Locals[index]);
                    }

                    else if (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S)
                    {
                        var index = instr.GetParameterIndex();
                        stack.Push(new AddressOfExpression(mc.Locals[index]));
                    }

                    else if (op == OpCodes.Ldnull)
                    {
                        stack.Push(Expression.Constant(null, typeof(object)));
                    }

                    else if (op == OpCodes.Ldobj)
                    {
                        var type = instr.GetParameterType();
                        var isVolatile = prefixes.HasVolatile;
                        var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Ldsfld)
                    {
                        var field = instr.GetParameterField();
                        object value;
                        if (MethodAnalyzer.IsConstantField(field, out value))
                        {
                            stack.Push(Expression.Constant(value, field.FieldType));
                        }
                        else
                        {
                            state = AddResultEffect(state.ClearStack(), true, Expression.Field(null, field));
                            stack.Push(state.Stack.Single());
                        }
                    }

                    else if (op == OpCodes.Ldsflda)
                    {
                        var field = instr.GetParameterField();
                        stack.Push(new AddressOfExpression(Expression.Field(null, field)));
                    }

                    else if (op == OpCodes.Ldstr)
                    {
                        var str = instr.GetParameter<string>();
                        stack.Push(Expression.Constant(str));
                    }

                    else if (op == OpCodes.Ldtoken)
                    {
                        var asFld = instr.Parameters[0] as FieldInfo;
                        var asMtd = instr.Parameters[0] as MethodInfo;
                        var asType = instr.Parameters[0] as Type;

                        if (asFld != null)
                        {
                            stack.Push(Expression.Constant(asFld.FieldHandle));
                        }
                        else if (asMtd != null)
                        {
                            stack.Push(Expression.Constant(asMtd.MethodHandle));
                        }
                        else if (asType != null)
                        {
                            stack.Push(Expression.Constant(asType.TypeHandle));
                        }
                        else throw new Exception("Unexpected operand for ldtoken [" + instr.Parameters[0] + "]");
                    }

                    else if (op == OpCodes.Ldvirtftn)
                    {
                        var mtd = instr.GetParameterMethod();
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Leave || op == OpCodes.Leave_S)
                    {
                        var label = instr.GetParameterLabel();
                        results.SetBranching(label); // TODO: should call finaly
                        break;
                    }

                    else if (op == OpCodes.Localloc)
                    {
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Mkrefany)
                    {
                        var type = instr.GetParameterType();
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Mul)
                    {
                        var a = stack.Pop();
                        var b = stack.Pop();
                        stack.Push(Expression.Multiply(a, b));
                    }

                    else if (op == OpCodes.Mul_Ovf)
                    {
                        var a = stack.Pop();
                        var b = stack.Pop();
                        stack.Push(Expression.MultiplyChecked(ConvertToSigned(a), ConvertToSigned(b)));
                    }

                    else if (op == OpCodes.Mul_Ovf_Un)
                    {
                        var a = stack.Pop();
                        var b = stack.Pop();
                        stack.Push(Expression.MultiplyChecked(ConvertToUnsigned(a), ConvertToUnsigned(b)));
                    }

                    else if (op == OpCodes.Neg)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Negate(p));
                    }

                    else if (op == OpCodes.Newarr)
                    {
                        var type = instr.GetParameterType();
                        var size = stack.Pop();
                        stack.Push(Expression.NewArrayBounds(type, size));
                    }

                    else if (op == OpCodes.Newobj)
                    {
                        var ctor = instr.GetParameter<ConstructorInfo>();
                        state = CallCtor(ctor, state.WithStack(stack));
                        stack = new Stack<Expression>(state.Stack);
                    }

                    else if (op == OpCodes.Nop)
                    {
                        // no-op
                    }

                    else if (op == OpCodes.Not)
                    {
                        var p = stack.Pop();
                        stack.Push(Expression.Not(p));
                    }

                    else if (op == OpCodes.Or)
                    {
                        var a = stack.Pop();
                        var b = stack.Pop();
                        stack.Push(Expression.Or(a, b));
                    }

                    else if (op == OpCodes.Pop)
                    {
                        stack.Pop(); // ignore
                    }

                    else if (op == OpCodes.Prefix1 || op == OpCodes.Prefix2 || op == OpCodes.Prefix3 || op == OpCodes.Prefix4 || op == OpCodes.Prefix5 || op == OpCodes.Prefix6 || op == OpCodes.Prefix7 || op == OpCodes.Prefixref)
                    {
                        throw new InvalidOperationException("Encountered reserved opcode [" + op + "]");
                    }

                    else if (op == OpCodes.Readonly)
                    {
                        prefixes.SetReadOnly();
                    }

                    else if (op == OpCodes.Refanytype)
                    {
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Refanyval)
                    {
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Rem)
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(Expression.Modulo(a, b));
                    }

                    else if (op == OpCodes.Rem_Un)
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(Expression.Modulo(ConvertToUnsigned(a), ConvertToUnsigned(b)));
                    }

                    else if (op == OpCodes.Ret)
                    {
                        if (stack.Count == 1)
                            results.SetReturn(stack.Pop());
                        else if (stack.Count == 0)
                            results.SetReturn();
                        else throw new InvalidOperationException();
                        break;
                    }


                    else if (op == OpCodes.Rethrow)
                    {
                        results.SetRethrow();
                        break;
                    }

                    else if (op == OpCodes.Shl)
                    {
                        var bits = stack.Pop();
                        var num = stack.Pop();
                        stack.Push(Expression.LeftShift(num, bits));
                    }

                    else if (op == OpCodes.Shr)
                    {
                        var bits = stack.Pop();
                        var num = stack.Pop();
                        stack.Push(Expression.RightShift(num, bits));
                    }

                    else if (op == OpCodes.Shr_Un)
                    {
                        var bits = stack.Pop();
                        var num = stack.Pop();
                        stack.Push(Expression.RightShift(num, bits));
                    }

                    else if (op == OpCodes.Sizeof)
                    {
                        var type = instr.GetParameterType();
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Starg || op == OpCodes.Starg_S)
                    {
                        ushort ix = instr.GetParameterIndex();
                        var arg = mc.Parameters[ix];
                        var val = stack.Pop();
                        SetVar(state.WithStack(stack), arg, val);
                        stack = new Stack<Expression>(state.Stack);
                    }

                    else if (op == OpCodes.Stelem)
                    {
                        var type = instr.GetParameterType();
                        var value = ImplicitConvertTo(stack.Pop(), type);
                        var index = stack.Pop();
                        var array = stack.Pop();
                        state = SetArrayElement(state, array, index, value);
                    }

                    //if (op == OpCodes.Stelem_I)
                    //{
                    //    var type = typeof(IntPtr);
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type },
                    //            Replay = emit => emit.StoreElement(type)
                    //        };
                    //}

                    //if (op == OpCodes.Stelem_I1)
                    //{
                    //    var type = typeof(sbyte);
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type },
                    //            Replay = emit => emit.StoreElement(type)
                    //        };
                    //}

                    //if (op == OpCodes.Stelem_I2)
                    //{
                    //    var type = typeof(short);
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type },
                    //            Replay = emit => emit.StoreElement(type)
                    //        };
                    //}

                    //if (op == OpCodes.Stelem_I4)
                    //{
                    //    var type = typeof(int);
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type },
                    //            Replay = emit => emit.StoreElement(type)
                    //        };
                    //}

                    //if (op == OpCodes.Stelem_I8)
                    //{
                    //    var type = typeof(long);
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type },
                    //            Replay = emit => emit.StoreElement(type)
                    //        };
                    //}

                    //if (op == OpCodes.Stelem_R4)
                    //{
                    //    var type = typeof(float);
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type },
                    //            Replay = emit => emit.StoreElement(type)
                    //        };
                    //}

                    //if (op == OpCodes.Stelem_R8)
                    //{
                    //    var type = typeof(double);
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type },
                    //            Replay = emit => emit.StoreElement(type)
                    //        };
                    //}

                    //if (op == OpCodes.Stelem_Ref)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = null,
                    //            Replay = emit => emit.StoreElement<WildcardType>()
                    //        };
                    //}

                    else if (op == OpCodes.Stfld)
                    {
                        var fld = (FieldInfo)instr.GetParameterField();
                        var val = stack.Pop();
                        var left = Expression.Field(stack.Pop(), fld);
                        state = SetVar(state.WithStack(stack), left, val);
                        stack = new Stack<Expression>(state.Stack);
                    }

                    //if (op == OpCodes.Stind_I)
                    //{
                    //    var type = typeof(IntPtr);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.StoreIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Stind_I1)
                    //{
                    //    var type = typeof(sbyte);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.StoreIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Stind_I2)
                    //{
                    //    var type = typeof(short);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.StoreIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Stind_I4)
                    //{
                    //    var type = typeof(int);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.StoreIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Stind_I8)
                    //{
                    //    var type = typeof(long);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.StoreIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Stind_R4)
                    //{
                    //    var type = typeof(float);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.StoreIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Stind_R8)
                    //{
                    //    var type = typeof(double);
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.StoreIndirect(type, isVolatile, unaligned)
                    //        };
                    //}

                    //if (op == OpCodes.Stind_Ref)
                    //{
                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = null,
                    //            Replay = emit => emit.StoreIndirect<WildcardType>(),

                    //            Prefixes = prefixes.Clone()
                    //        };
                    //}

                    else if (op == OpCodes.Stloc || op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1 || op == OpCodes.Stloc_2 || op == OpCodes.Stloc_3 || op == OpCodes.Stloc_S)
                    {
                        var ix = instr.GetParameterIndex();
                        var loc = mc.Locals[ix];
                        var val = stack.Pop();
                        state = SetVar(state.WithStack(stack), loc, val);
                        stack = new Stack<Expression>(state.Stack);
                    }

                    //if (op == OpCodes.Stobj)
                    //{
                    //    var type = (Type)operands[0];
                    //    var isVolatile = prefixes.HasVolatile;
                    //    var unaligned = prefixes.HasUnaligned ? prefixes.Unaligned : null;

                    //    return
                    //        new Operation<DelegateType>
                    //        {
                    //            OpCode = op,
                    //            Parameters = new object[] { type, isVolatile, unaligned },
                    //            Replay = emit => emit.StoreObject(type, isVolatile, unaligned)
                    //        };
                    //}

                    else if (op == OpCodes.Stsfld)
                    {
                        var fld = instr.GetParameterField();
                        var value = stack.Pop();
                        state = AddResultEffect(state, false, Expression.Assign(Expression.Field(null, fld), value));
                    }

                    else if (op == OpCodes.Sub)
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(Expression.Subtract(a, b));
                    }

                    else if (op == OpCodes.Sub_Ovf)
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(Expression.SubtractChecked(ConvertToSigned(a), ConvertToSigned(b)));
                    }

                    else if (op == OpCodes.Sub_Ovf_Un)
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(Expression.SubtractChecked(ConvertToUnsigned(a), ConvertToUnsigned(b)));
                    }

                    else if (op == OpCodes.Switch)
                    {
                        var labels = (Sigil.Label[])instr.Parameters[0];
                        var val = stack.Pop().Resolve(state);
                        if (val.IsConstant())
                        {
                            var num = (val.GetConstantValue() is IConvertible) ? ((IConvertible)val.GetConstantValue()).ToInt32(null) : (int)(dynamic)val.GetConstantValue();
                            if (num < labels.Length)
                            {
                                results.SetBranching(labels[num]);
                                break;
                            }
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }

                    else if (op == OpCodes.Tailcall)
                    {
                        prefixes.SetTailCall();

                    }

                    else if (op == OpCodes.Throw)
                    {
                        results.SetThrow(stack.Pop());
                        break;
                    }

                    else if (op == OpCodes.Unaligned)
                    {
                        byte u = instr.GetParameter<byte>();
                        prefixes.SetUnaligned(u);
                    }

                    else if (op == OpCodes.Unbox)
                    {
                        var type = instr.GetParameterType();
                        throw new NotSupportedException();
                    }

                    else if (op == OpCodes.Unbox_Any)
                    {
                        var type = instr.GetParameterType();
                        var obj = stack.Pop();
                        if (obj.Type.IsValueType)
                        {
                            Debug.Assert(obj.Type == type);
                            stack.Push(obj);
                        }
                        else stack.Push(Expression.Unbox(obj, type));
                    }

                    else if (op == OpCodes.Volatile)
                    {
                        prefixes.SetVolatile();
                    }

                    else if (op == OpCodes.Xor)
                    {
                        var a = stack.Pop();
                        var b = stack.Pop();
                        stack.Push(Expression.ExclusiveOr(a, b));
                    }

                    else throw new Exception("Unexpected opcode [" + op + "]");
                }
                else if (instr.IsMarkLabel)
                {
                    // nothing
                }
                else if (instr.IsExceptionBlockEnd || instr.IsFinallyBlockEnd || instr.IsFinallyBlockStart || instr.IsExceptionBlockStart)
                {
                    // TODO: exception blocks
                }
                else if (instr.IsFaultBlockStart)
                {
                    // TODO: exceptions
                    // ignore catch blocks
                    do
                    {
                        eip++;
                        Debug.Assert(!instructions[eip].IsFaultBlockStart);
                    }
                    while (!instructions[eip].IsFaultBlockEnd);
                }
                else if (instr.IsCatchBlockStart)
                {
                    // TODO: exceptions
                    do
                    {
                        eip++;
                        Debug.Assert(!instructions[eip].IsCatchBlockStart);
                    }
                    while (!instructions[eip].IsCatchBlockEnd);
                }
                else throw new NotImplementedException();

                if (results.IsSet) break;
                eip++;
            }
            Debug.Assert(results.IsSet);

            if (results.Branch != null)
            {
                state = state.WithStack(stack).ResolveStack();
                var condition = results.Branch.Resolve(state);
                Debug.Assert(condition.Type == typeof(bool));
                if (condition.NodeType == ExpressionType.Constant)
                {
                    if ((bool)condition.GetConstantValue())
                    {
                        return ExecCore(mc, state, FindLabel(mc.Disassembly, results.BranchTo));
                    }
                    else
                    {
                        return ExecCore(mc, state, eip + 1);
                    }
                }
                else
                {
                    var stateTrue = state.Nest(condition);
                    var stateFalse = state.Nest(Expression.Not(condition).Simplify());

                    stateTrue = ExecCore(mc, stateTrue, FindLabel(mc.Disassembly, results.BranchTo));
                    stateFalse = ExecCore(mc, stateFalse, eip + 1);

                    return MergeState(state, condition, stateTrue, stateFalse);
                }
            }

            Debug.Assert(stack.Count == 0);
            if (results.Throw != null) return state.ClearStack().WithException(results.Throw);
            if (results.Rethrow) throw new NotImplementedException();
            if (results.Return != null)
            {
                if (results.Return.NodeType == ExpressionType.Default && results.Return.Type == typeof(void))
                    return state.WithStack(new Expression[0]);
                else return state.WithStack(new[] { ImplicitConvertTo(results.Return, mc.Disassembly.ReturnType).Resolve(state) });
            }

            return state;
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static ExecutionState MergeState(ExecutionState root, Expression condition, ExecutionState trueBranch, ExecutionState falseBranch)
        {
            trueBranch = trueBranch.ResolveStack();
            falseBranch = falseBranch.ResolveStack();
            var addAssignments = new List<KeyValuePair<Expression, Expression>>();
            foreach (var assignment in trueBranch.SetExpressions)
            {
                Expression elseExpression;
                if (!falseBranch.SetExpressions.TryGetValue(assignment.Key, out elseExpression))
                {
                    if (!root.SetExpressions.TryGetValue(assignment.Key, out elseExpression))
                    {
                        if (assignment.Key.NodeType == ExpressionType.Parameter && ((ParameterExpression)assignment.Key).Name?.StartsWith("__") != false)
                        {
                            elseExpression = null; // if false nor root contain any set and it is temp parameter - do not introduce condition
                        }
                        else
                        {
                            elseExpression = Expression.Default(assignment.Value.Type);
                        }
                    }
                    else elseExpression = elseExpression.Resolve(root);
                }
                else elseExpression = elseExpression.Resolve(falseBranch);

                var rightExpression = assignment.Value.Resolve(trueBranch);
                var expr = elseExpression == null ? rightExpression : Expression.Condition(condition, rightExpression, elseExpression);
                addAssignments.Add(new KeyValuePair<Expression, Expression>(assignment.Key, expr.Simplify()));
            }
            foreach (var assignment in falseBranch.SetExpressions)
            {
                if (trueBranch.SetExpressions.ContainsKey(assignment.Key)) continue; // it has been already added in the first cycle
                Expression elseExpression;
                if (!root.SetExpressions.TryGetValue(assignment.Key, out elseExpression))
                {
                    if (assignment.Key.NodeType == ExpressionType.Parameter && ((ParameterExpression)assignment.Key).Name?.StartsWith("__") != false)
                    {
                        elseExpression = null; // if false nor root contain any set and it is temp parameter - do not introduce condition
                    }
                    else
                    {
                        elseExpression = Expression.Default(assignment.Value.Type);
                    }
                }
                else elseExpression = elseExpression.Resolve(root);

                var rightExpression = assignment.Value.Resolve(falseBranch);
                var expr = elseExpression == null ? rightExpression : Expression.Condition(condition, elseExpression, rightExpression);
                addAssignments.Add(new KeyValuePair<Expression, Expression>(assignment.Key, expr.Simplify()));
            }
            root = root.WithSets(addAssignments);



            var trueBranchException = trueBranch.FindThrowException();
            var falseBranchException = falseBranch.FindThrowException();
            Debug.Assert(trueBranch.HasStack() == falseBranch.HasStack() || falseBranchException != null || trueBranchException != null);
            if (trueBranch.HasStack() && falseBranch.HasStack())
            {
                Debug.Assert(trueBranch.Stack.Length == falseBranch.Stack.Length);
                root = root.WithStack(trueBranch.Stack.Zip(falseBranch.Stack, (t, f) => Expression.Condition(condition, t, f).Simplify()));
            }
            else if (trueBranch.HasStack())
            {
                root = root.WithStack(trueBranch.Stack);
            }
            else if (falseBranch.HasStack())
            {
                root = root.WithStack(falseBranch.Stack);
            }

            var notCondition = Expression.Not(condition).Simplify();
            root = root.AddSideEffects(trueBranch.SideEffects.Select(k => new KeyValuePair<Expression, Expression>(k.Key == null ? condition : Expression.And(condition, k.Key), k.Value)));
            root = root.AddSideEffects(falseBranch.SideEffects.Select(k => new KeyValuePair<Expression, Expression>(k.Key == null ? notCondition : Expression.And(notCondition, k.Key), k.Value)));

            return root;
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public ExecutionState CallMethod(MethodBase method, ExecutionState state, bool virt, Type constrained = null)
        {
            Debug.Assert(state.Stack.Length >= method.GetParameters().Length + (method.IsStatic ? 0 : 1));
            state = state.ResolveStack();
            var stack = state.Stack;
            var args = new List<Expression>();
            var methodParameters = method.GetParameters();
            if (!method.IsStatic) args.Add(UnwrapAddressOf(stack[stack.Length - methodParameters.Length - 1]));
            args.AddRange(methodParameters.Zip(stack.Skip(stack.Length - methodParameters.Length), (p, a) => ImplicitConvertTo(a, p.ParameterType).Simplify()));

            Debug.Assert(args.Count == methodParameters.Length + (method.IsStatic ? 0 : 1));

            //int instanceOffset = method.IsStatic ? 0 : 1;
            //for (int i = 0; i < methodParameters.Length; i++)
            //{
            //    if (args[i + instanceOffset].Type != methodParameters[i].ParameterType) args[i + instanceOffset] = ImplicitConvertTo(args[i + instanceOffset], methodParameters[i].ParameterType);
            //}

            //for (int i = 0; i < args.Count; i++)
            //{
            //    args[i] = args[i].Simplify();
            //}

            if (method.DeclaringType.IsInterface)
            {
                var vvv = MethodAnalyzer.GetSpecialExecutor(method, args)?.Invoke(this, state, args, method);
                if (vvv != null) return vvv;
                if (MethodAnalyzer.IsResultEffect(method))
                {
                    return AddResultEffect(method, state, args);
                }
            }

            if (!method.IsStatic)
            {
                Type instanceType = args[0].Type;
                var reallyVirtual = virt && method.IsVirtual && !method.IsFinal;
                if ((args[0].NodeType == ExpressionType.Default && !args[0].Type.IsValueType) || (args[0].NodeType == ExpressionType.Constant && args[0].GetConstantValue() == null))
                {
                    return state.WithException(Expression.New(typeof(NullReferenceException)));
                }
                if (reallyVirtual)
                {
                    if (!instanceType.IsSealed && !(instanceType.IsByRef && instanceType.GetElementType().IsValueType))
                        instanceType = args[0].Resolve(state).ProveType(state);
                    if (instanceType == null)
                    {
                        throw new NotImplementedException("Could not prove virtual call method");
                    }
                    if (method.DeclaringType != instanceType)
                    {
                        var baseMethod = (MethodInfo)method;
                        method = instanceType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                            .Single(m => m.IsOverrideOf(baseMethod)); // TODO: not sure if it works on private/static methods
                    }
                    Debug.Assert(method != null);
                }
            }

            if (!method.IsStatic && args[0].Type != method.DeclaringType)
            {
                if (args[0] is AddressOfExpression && args[0].Type.GetElementType() == method.DeclaringType)
                    args[0] = ((AddressOfExpression)args[0]).Object;
                else args[0] = ImplicitConvertTo(args[0], method.DeclaringType, force: true).Simplify();
            }

            var vv = MethodAnalyzer.GetSpecialExecutor(method, args)?.Invoke(this, state, args, method);
            if (vv != null) return vv;
            if (MethodAnalyzer.IsPure(method) && method is MethodInfo)
            {
                var f = CreateCallExpression((MethodInfo)method, args);
                Expression ff = f;
                if (args.All(a => a.IsConstant()))
                {
                    ff = Expression.Constant(f.Method.Invoke(f.Object?.GetConstantValue(), f.Arguments.Select(ExpressionUtils.GetConstantValue).ToArray()));
                }
                return state.ReplaceTopStack(args.Count, new[] { ff });
            }
            if (MethodAnalyzer.IsResultEffect(method))
            {
                return AddResultEffect(method, state, args);
            }

            Debug.Assert(!method.DeclaringType.IsInterface);
            Debug.Assert(method.GetMethodBody() != null);

            var disassembly = Disassembly(method);
            var methodName = method.Name;
            var dd = disassembly.ToString(); // DEBUG
            state = Execute(state, disassembly, args);
            if (state.Stack.Length > 0 && stack.Length != args.Count)
            {
                return state.WithStack(stack.Take(stack.Length - args.Count).Concat(state.Stack));
            }
            else if (stack.Length != args.Count)
            {
                return state.WithStack(stack.Take(stack.Length - args.Count));
            }
            else return state;
        }

        public ExecutionState AddResultEffect(MethodBase method, ExecutionState state, List<Expression> args)
        {
            var hasResult = (method is MethodInfo && ((MethodInfo)method).ReturnType != typeof(void));
            var eff = CreateCallExpression((MethodInfo)method, args).Simplify();
            return AddResultEffect(state.ReplaceTopStack(args.Count, new Expression[0]), hasResult, eff);
        }

        public ExecutionState AddResultEffect(ExecutionState state, bool hasResult, Expression eff)
        {
            //Debug.Assert(eff == eff.Simplify());
            var newstack = hasResult ? new[] { MyExpression.RootParameter(eff.Type, "__sideEffectParam_" + Interlocked.Increment(ref pcnt)) } : new Expression[0];
            if (hasResult) eff = Expression.Assign(newstack[0], eff);
            return state.AddSideEffect(eff).ReplaceTopStack(0, newstack);
        }

        public MethodCallExpression CreateCallExpression(MethodInfo method, IEnumerable<Expression> parameters)
        {
            if (method.IsStatic) return Expression.Call(method, parameters);
            else return Expression.Call(parameters.First(), method, parameters.Skip(1));
        }

        public ExecutionState CallCtor(ConstructorInfo ctor, ExecutionState state)
        {
            // TODO: stack handling is probably wrong
            var parameters = ctor.GetParameters();
            var leftstack = state.Stack.Take(state.Stack.Length - parameters.Length).ToArray();
            var stack = new Stack<Expression>(state.Stack.Skip(state.Stack.Length - parameters.Length).Reverse());
            Debug.Assert(stack.Count == parameters.Length);

            var args = parameters.Select(p => ImplicitConvertTo(stack.Pop(), p.ParameterType).Resolve(state)).ToArray();
            var thisExpression = MyExpression.RootParameter(ctor.DeclaringType, "__this" + Interlocked.Increment(ref pcnt), notNull: true, exactType: true);

            if (typeof(Delegate).IsAssignableFrom(ctor.DeclaringType))
            {
                Debug.Assert(args[1] is FunctionPointerExpression);
                state = state.WithSet(thisExpression, Expression.New(ctor, args));
            }
            else if (MethodAnalyzer.IsPure(ctor) && args.All(a => a.NodeType == ExpressionType.Constant))
            {
                state = state.WithSet(thisExpression, Expression.Constant(ctor.Invoke(args.Select(a => a.GetConstantValue()).ToArray())));
            }
            else
            {
                state = state.WithSet(thisExpression, Expression.New(ctor, args));

                state = InitObject(state, thisExpression);

                var specExec = MethodAnalyzer.GetSpecialExecutor(ctor, args);
                if (specExec != null)
                {
                    state = specExec(this, state, args, ctor);
                }
                else
                {
                    var disassembly = Disassembly(ctor);
                    state = Execute(state, disassembly, new[] { thisExpression }.Concat(args).ToArray());
                }
            }
            return state.WithStack(leftstack.Concat(new[] { thisExpression }));
        }

        public ExecutionState InitObject(ExecutionState state, Expression thisExpression)
        {
            foreach (var fld in thisExpression.Type.GetAllFields())
            {
                state = state.WithSet(Expression.Field(thisExpression, fld), Expression.Default(fld.FieldType).Simplify());
            }
            return state;
        }

        public static int FindLabel(DisassemblyResult disassembly, Sigil.Label label)
        {
            for (int eip = 0; eip < disassembly.Instructions.Length; eip++)
            {
                var ist = disassembly.Instructions[eip];
                if (ist.IsMarkLabel && ist.Parameters.Single() == label) return eip;
            }
            throw new Exception($"Label {label.Name} not found.");
        }

        public static ExecutionState SetVar(ExecutionState state, Expression left)
        {
            var right = state.Stack.Last();
            return SetVar(state, left, right, stackSkip: 1);
        }

        public static ExecutionState SetVar(ExecutionState state, Expression left, Expression right, int stackSkip = 0)
        {
            right = right.Resolve(state);

            left = left.Resolve(state, paramOnly: true);

            var stackcp = state.Stack.Take(state.Stack.Length - stackSkip)
                 .Select(e => e.Resolve(state))
                 .Select(e => AddEnvParameter(ref state, e)) // when unresolved memberAccess, or whatever, it is side effect
                 .ToArray();
            state = state.WithStack(stackcp);

            if (left is MyParameterExpression || left.NodeType == ExpressionType.MemberAccess)
            {
                return state.WithSets(SetVarCore(left, right));
            }
            else if (left is ConditionalExpression)
            {
                var sets = ((ConditionalExpression)left).EnumerateBranches()
                    .SelectMany(branch =>
                    {
                        var leftValue = state.TryFindAssignment(branch.IfTrue);
                        Debug.Assert(leftValue != null);
                        right = ImplicitConvertTo(right, leftValue.Type);
                        var value = Expression.Condition(branch.Test, right, leftValue);
                        return SetVarCore(branch.IfTrue, value);
                    });
                return state.WithSets(sets);
            }
            else
            {
                throw new NotSupportedException($"Assigning to expression '{left.GetType().Name}' is not supported.");
            }
        }

        public static string NameParam(string name) => name + Interlocked.Increment(ref pcnt);

        static KeyValuePair<Expression, Expression>[] SetVarCore(Expression left, Expression right)
        {
            if (left is MyParameterExpression)
            {
                Debug.Assert(((MyParameterExpression)left).IsMutable);
            }

            // add root parameter for mutable value types or when right is not parameter (unless right is primitive constant)
            if ((right is MyParameterExpression || (right.IsConstant() && (right.Type.IsPrimitive || right.GetConstantValue() == null))) &&
                (!right.Type.IsValueType || right.Type.IsPrimitive || true)) // TODO: handle mutable structs correctly
            {
                if (right.Type != left.Type)
                {
                    right = ImplicitConvertTo(right, left.Type) ?? Expression.Convert(right, left.Type);
                }

                return new[] { new KeyValuePair<Expression, Expression>(left, right) };
            }
            else
            {
                Expression parameterExpression = MyExpression.RootParameter(right.Type, NameParam("__setPar"));

                Expression rightParameter = parameterExpression;
                if (right.Type != left.Type)
                {
                    rightParameter = ImplicitConvertTo(right, left.Type) ?? Expression.Convert(rightParameter, left.Type);
                }

                return new[]
                {
                    new KeyValuePair<Expression, Expression>(parameterExpression, right),
                    new KeyValuePair<Expression, Expression>(left, rightParameter)
                };
            }
        }

        public static ExecutionState SetArrayElement(ExecutionState state, Expression array, Expression index, Expression value)
        {
            index = index.Resolve(state);
            array = array.Resolve(state);
            value = value.Resolve(state);
            if (index.IsConstant())
            {
                return state.WithSet(Expression.ArrayIndex(array, index), value);
            }
            else throw new NotImplementedException();
        }

        private static Expression AddEnvParameter(ref ExecutionState state, Expression expr)
        {
            if (expr.NodeType == ExpressionType.MemberAccess)
            {
                var par = MyExpression.RootParameter(expr.Type, $"__envPar<{expr.ToString()}>");
                state.AddSideEffect(Expression.Assign(par, expr));
                return par;
            }
            else return expr;
        }
    }

    public class MethodExecutionInfo
    {
        public DisassemblyResult Disassembly { get; }
        public MyParameterExpression[] Locals { get; }
        public MyParameterExpression[] Parameters { get; }

        public MethodExecutionInfo(DisassemblyResult disassembly, MyParameterExpression[] locals, MyParameterExpression[] parameters)
        {
            this.Disassembly = disassembly;
            this.Locals = locals;
            this.Parameters = parameters;
        }

        //public Dictionary<Expression, ParameterExpression> EnvironmentParameters { get; set; } = new Dictionary<Expression, ParameterExpression>(ExpressionComparer.Instance);
    }

    public class DisassemblyResult
    {
        public MethodInstruction[] Instructions { get; }
        public Sigil.Label[] Labels { get; }
        public Sigil.Local[] Locals { get; }
        public Sigil.Parameter[] Parameters { get; }
        public Type ReturnType { get; }

        public DisassemblyResult(MethodInstruction[] instructions, Sigil.Label[] labels, Sigil.Local[] locals, Sigil.Parameter[] parameters, Type returnType)
        {
            this.Instructions = instructions;
            this.Labels = labels;
            this.Locals = locals;
            this.Parameters = parameters;
            this.ReturnType = returnType;
        }

        public override string ToString()
        {
            return $@"
params(
    {string.Join(",\n    ", (IEnumerable<object>)Parameters)})
locals(
    {string.Join(",\n    ", (IEnumerable<object>)Locals)})
labels(
    {string.Join(",\n    ", (IEnumerable<object>)Labels)})

{string.Join("\n", (IEnumerable<object>)Instructions)}
";
        }
    }

    public enum InstructionType
    {
        None, OpCode, MarkLabel, ExceptionBlockStart, ExceptionBlockEnd, CatchBlockStart, CatchBlockEnd, FinallyBlockStart, FinallyBlockEnd, FaultBlockStart, FaultBlockEnd
    }

    public class MethodInstruction
    {
        public InstructionType Type { get; }

        //
        // Summary:
        //     This operation marks the end of a catch block, which is analogous to a call to
        //     Emit.EndCatchBlock.
        public bool IsCatchBlockEnd => Type == InstructionType.CatchBlockEnd;
        //
        // Summary:
        //     This operation marks the beginning of a catch block, which is analogous to a
        //     call to Emit.BeginCatchBlock.
        public bool IsCatchBlockStart => Type == InstructionType.CatchBlockStart;
        //
        // Summary:
        //     This operation marks the end of an exception block, which is analogous to a call
        //     to Emit.EndExceptionBlock.
        public bool IsExceptionBlockEnd => Type == InstructionType.ExceptionBlockEnd;
        //
        // Summary:
        //     This operation marks the beginning of an exception block, which is analogous
        //     to a call to Emit.BeginExceptionBlock.
        public bool IsExceptionBlockStart => Type == InstructionType.ExceptionBlockStart;
        //
        // Summary:
        //     This operation marks the end of a finally block, which is analogous to a call
        //     to Emit.EndFinallyBlock.
        public bool IsFinallyBlockEnd => Type == InstructionType.FinallyBlockEnd;
        //
        // Summary:
        //     This operation marks the beginning of a finally block, which is analogous to
        //     a call to Emit.BeginFinallyBlock.
        public bool IsFinallyBlockStart => Type == InstructionType.FinallyBlockStart;

        public bool IsFaultBlockStart => Type == InstructionType.FaultBlockStart;
        public bool IsFaultBlockEnd => Type == InstructionType.FinallyBlockEnd;
        //
        // Summary:
        //     This operation marks a label, the name of the label is given in LabelName.
        public bool IsMarkLabel => Type == InstructionType.MarkLabel;
        //
        // Summary:
        //     Returns true if this operation is emitted a CIL opcode.
        public bool IsOpCode => Type == InstructionType.OpCode;
        //
        // Summary:
        //     If this operation marks a label, which is indicated by IsMarkLabel, then this
        //     property returns the name of the label being marked.
        public string LabelName { get; }
        //
        // Summary:
        //     The OpCode that corresponds to an Emit call. Note that the opcode may not correspond
        //     to the final short forms and other optimizations.
        public OpCode OpCode { get; }
        //
        // Summary:
        //     The parameters passsed to a call to Emit.
        public object[] Parameters { get; }

        public MethodInstruction(InstructionType type, string labelName = null, OpCode opCode = default(OpCode), object[] parameters = null)
        {
            this.Type = type;
            this.LabelName = labelName;
            this.OpCode = opCode;
            this.Parameters = parameters;
        }

        public Sigil.Label GetParameterLabel()
        {
            Debug.Assert(Parameters.Length == 1);
            return (Sigil.Label)Parameters[0];
        }

        public Type GetParameterType()
        {
            ValidateParameters(true);
            return (Type)Parameters[0];
        }

        public MethodBase GetParameterMethod(out Type constrained)
        {
            constrained = null;
            if (Parameters.Length > 1)
            {
                constrained = (Type)Parameters[1];
            }
            return (MethodBase)Parameters[0];
        }

        public MethodBase GetParameterMethod()
        {
            return (MethodBase)Parameters[0];
        }

        private void ValidateParameters(bool allowboolint)
        {
            Debug.Assert(Parameters.Length == 1 || (allowboolint && Parameters.Length == 3 && Parameters[1] is bool && (Parameters[2] == null || Parameters[2] is int)));
        }

        public FieldInfo GetParameterField()
        {
            ValidateParameters(true);
            return (FieldInfo)Parameters[0];
        }

        public ushort GetParameterIndex()
        {
            Debug.Assert(Parameters.Length == 1);
            if (Parameters[0] is Sigil.Local) return ((Sigil.Local)Parameters[0]).Index;
            return (ushort)Parameters[0];
        }

        public Sigil.Local GetParameterLocal()
        {
            Debug.Assert(Parameters.Length == 1);
            return (Sigil.Local)Parameters[0];
        }

        public T GetParameter<T>()
        {
            ValidateParameters(true);
            return (T)Parameters[0];
        }

        public override string ToString()
        {
            if (IsOpCode)
            {
                return $"{OpCode.Name} " + string.Join(", ", Parameters);
            }
            else if (IsExceptionBlockStart)
            {
                return "try {";
            }
            else if (IsExceptionBlockEnd)
            {
                return "} // try";
            }
            else if (IsFinallyBlockStart)
            {
                return "finally {";
            }
            else if (IsFinallyBlockEnd)
            {
                return "} // finally";
            }
            else if (IsCatchBlockStart)
            {
                return "catch {" + string.Join(", ", Parameters);
            }
            else if (IsCatchBlockEnd)
            {
                return "} // catch";
            }
            else if (IsMarkLabel)
            {
                return "LABEL: " + LabelName;
            }
            return "{{unknown instruction}}";
        }
    }
}
