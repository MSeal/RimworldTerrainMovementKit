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
        // Taken from https://github.com/pardeike/Zombieland/blob/d1f558ca159cc56566e505b56532e40ec0f5ec89/Source/SafeReflections.cs, thank you @brrainz for the pointers in your discord!
        public static FieldInfo Field(this Type type, string fieldName)
        {
            var field = AccessTools.Field(type, fieldName);
            if (field == null) throw new Exception("Cannot find field '" + fieldName + "' in type " + type.FullName);
            return field;
        }

        public static Type InnerTypeStartingWith(this Type type, string prefix)
        {
            var innerType = AccessTools.FirstInner(type, subType => subType.Name.StartsWith(prefix));
            if (innerType == null) throw new Exception("Cannot find inner type starting with '" + prefix + "' in type " + type.FullName);
            return innerType;
        }

        public static MethodInfo MethodMatching(this Type type, Func<MethodInfo[], MethodInfo> predicate)
        {
            var method = predicate(type.GetMethods(AccessTools.all));
            if (method == null) throw new Exception("Cannot find method matching " + predicate + " in type " + type.FullName);
            return method;
        }

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

        public static List<CodeInstruction> PreCallReplaceFunctionArgument(List<CodeInstruction> opsUpToCall, MethodInfo newMethod, IEnumerable<CodeInstruction> addedArguments, int distanceFromRight)
        {
            List<CodeInstruction> rightMostArgs = new List<CodeInstruction>();
            // Pop the last few parameters
            for (int i = 0; i < distanceFromRight; i++)
            {
                rightMostArgs.Insert(0, opsUpToCall.Pop());
            }
            // Add the new parameter arguments
            foreach (CodeInstruction arg in addedArguments)
            {
                opsUpToCall.Add(arg);
            }
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
                (opsUpToCall, _callInstruction) => PreCallReplaceFunctionArgument(opsUpToCall, newMethod, new List<CodeInstruction>() { addedArgument }, distanceFromRight),
                patchCallName,
                originalFuncName,
                null,
                repeat);
        }

        public static IEnumerable<CodeInstruction> ReplaceFunctionArgument(this IEnumerable<CodeInstruction> instructions, MethodInfo newMethod, IEnumerable<CodeInstruction> addedArguments, int distanceFromRight, string patchCallName, string originalFuncName, bool repeat = true)
        {
            return ReplaceFunction(
                instructions,
                (opsUpToCall, _callInstruction) => PreCallReplaceFunctionArgument(opsUpToCall, newMethod, addedArguments, distanceFromRight),
                patchCallName,
                originalFuncName,
                null,
                repeat);
        }
    }
}
