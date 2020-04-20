using HarmonyLib;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace TerrainMovement
{
    public static class ReflectionExtension
    {
        // Taken from http://kennethxu.blogspot.com/2009/05/strong-typed-high-performance.html -- Thank you for that how-to!
        public static DynamicMethod CreateNonVirtualDynamicMethod(this MethodInfo method)
        {
            int offset = (method.IsStatic ? 0 : 1);
            var parameters = method.GetParameters();
            int size = parameters.Length + offset;
            Type[] types = new Type[size];
            if (offset > 0) types[0] = method.DeclaringType;
            for (int i = offset; i < size; i++)
            {
                types[i] = parameters[i - offset].ParameterType;
            }

            DynamicMethod dynamicMethod = new DynamicMethod(
                "NonVirtualInvoker_" + method.Name, method.ReturnType, types, method.DeclaringType);
            ILGenerator il = dynamicMethod.GetILGenerator();
            for (int i = 0; i < types.Length; i++) il.Emit(OpCodes.Ldarg, i);
            il.EmitCall(OpCodes.Call, method, null);
            il.Emit(OpCodes.Ret);
            return dynamicMethod;
        }

        // Helper for defining function signature needed for function replacement
        public delegate List<CodeInstruction> CodeInstructionReplacementFunction(List<CodeInstruction> opsUpToCall, CodeInstruction callInstruction);

        // Used to replace a function call in a transpiler patch
        public static IEnumerable<CodeInstruction> ReplaceFunction(this IEnumerable<CodeInstruction> instructions, CodeInstructionReplacementFunction func, string patchCallName, string originalFuncName, OpCode? codeToMatch = null, bool repeat = true)
        {
            if (codeToMatch == null)
            {
                codeToMatch = OpCodes.Call;
            }
            bool found = false;
            List<CodeInstruction> runningChanges = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in instructions)
            {
                // Find the section calling TryFindRandomPawnEntryCell
                if ((repeat || !found) && instruction.opcode == codeToMatch && (instruction.operand as MethodInfo)?.Name == patchCallName)
                {
                    runningChanges = func(runningChanges, instruction).ToList();
                    found = true;
                }
                else
                {
                    runningChanges.Add(instruction);
                }
            }
            if (!found)
            {
                Log.ErrorOnce(String.Format("[TerrainMovementKit] Cannot find {0} in {1}, skipping patch", patchCallName, originalFuncName), patchCallName.GetHashCode() + originalFuncName.GetHashCode());
            }
            return runningChanges.AsEnumerable();
        }

        public static List<CodeInstruction> PreCallReplaceFunctionArgument(List<CodeInstruction> opsUpToCall, MethodInfo newMethod, CodeInstruction addedArgument, int distanceFromRight)
        {
            List<CodeInstruction> rightMostArgs = new List<CodeInstruction>();
            // Pop the last few parameters
            for (int i = 0; i < distanceFromRight; i++)
            {
                rightMostArgs.Insert(0, opsUpToCall.Pop());
            }
            // Add the new parameter
            opsUpToCall.Add(addedArgument);
            // Add the last few parameters back
            opsUpToCall.AddRange(rightMostArgs);
            // Add call to new method
            opsUpToCall.Add(new CodeInstruction(OpCodes.Call, newMethod));
            return opsUpToCall;
        }

        public static IEnumerable<CodeInstruction> ReplaceFunctionArgument(this IEnumerable<CodeInstruction> instructions, MethodInfo newMethod, CodeInstruction addedArgument, int distanceFromRight, string patchCallName, string originalFuncName, bool repeat = true)
        {
            return ReplaceFunction(
                instructions,
                (opsUpToCall, _callInstruction) => PreCallReplaceFunctionArgument(opsUpToCall, newMethod, addedArgument, distanceFromRight),
                patchCallName,
                originalFuncName,
                null,
                repeat);
        }
    }
}
