﻿using Harmony;
using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace StardewHack
{
    /** Common Hack code */
    public abstract class HackBase : Mod
    {
        /** Provides simplified API's for writing mods. */
        public IModHelper helper { get; private set; }

        /** The harmony instance used for patching. */
        public HarmonyInstance harmony { get; private set; }

        /** The method being patched. 
         * Use only within methods annotated with BytecodePatch. 
         */
        public MethodBase original { get; internal set; }

        /** The code that is being patched. 
         * Use only within methods annotated with BytecodePatch. 
         */
        public List<CodeInstruction> instructions { get; internal set; }

        /** The generator used for patching. 
         * Use only within methods annotated with BytecodePatch. 
         */
        public ILGenerator generator { get; internal set; }

        public override void Entry(IModHelper helper) {
            this.helper = helper;

            // Use the Mod's UniqueID to create the harmony instance.
            string UniqueID = helper.ModRegistry.ModID;
            Monitor.Log($"Applying bytecode patches for {UniqueID}.", LogLevel.Debug);
            harmony = HarmonyInstance.Create(UniqueID);
        }

        /** Find the first occurance of the given sequence of instructions that follows this range.
         * The performed matching depends on the type:
         *  - String: is it contained in the string representation of the instruction
         *  - MemberReference (including MethodDefinition): is the instruction's operand equal to this reference.
         *  - OpCode: is this the instruction's OpCode.
         *  - CodeInstruction: are the instruction's OpCode and Operand equal.
         *  - null: always matches.
         */
        public InstructionRange FindCode(params Object[] contains) {
            return new InstructionRange(instructions, contains);
        }

        /** Find the last occurance of the given sequence of instructions that follows this range.
         * See FindCode() for how the matching is performed.
         */
        public InstructionRange FindCodeLast(params Object[] contains) {
            return new InstructionRange(instructions, contains, instructions.Count, -1);
        }

        public InstructionRange BeginCode() {
            return new InstructionRange(instructions, 0, 0);
        }

        public InstructionRange EndCode() {
            return new InstructionRange(instructions, instructions.Count, 0);
        }

        public InstructionRange AllCode() {
            return new InstructionRange(instructions, 0, instructions.Count);
        }

        public Label AttachLabel(CodeInstruction target) {
            var lbl = generator.DefineLabel();
            target.labels.Add(lbl);
            return lbl;
        }

        /** Maps all short style branches to their long form. */
        Dictionary<OpCode, OpCode> long_map = new Dictionary<OpCode, OpCode>()
        {
            { OpCodes.Beq_S, OpCodes.Beq },
            { OpCodes.Bge_S, OpCodes.Bge },
            { OpCodes.Bgt_S, OpCodes.Bgt },
            { OpCodes.Ble_S, OpCodes.Ble },
            { OpCodes.Blt_S, OpCodes.Blt },
            { OpCodes.Bge_Un_S, OpCodes.Bge_Un },
            { OpCodes.Bgt_Un_S, OpCodes.Bgt_Un },
            { OpCodes.Ble_Un_S, OpCodes.Ble_Un },
            { OpCodes.Blt_Un_S, OpCodes.Blt_Un },
            { OpCodes.Bne_Un_S, OpCodes.Bne_Un },
            { OpCodes.Br_S, OpCodes.Br },
            { OpCodes.Brfalse_S, OpCodes.Brfalse },
            { OpCodes.Brtrue_S, OpCodes.Brtrue },
            { OpCodes.Leave_S, OpCodes.Leave },
        };

        /** Converts all short style branches into long branches. */
        public void ReplaceShortBranches() {
            for (int i=0; i<instructions.Count; i++) {
                var inst = instructions[i];
                if (long_map.ContainsKey(inst.opcode)) {
                    inst.opcode = long_map[inst.opcode];
                }
            }
        }
    }

    // I 'love' generics. :P
    // Used to have a separate static instance variable per type T.
    public abstract class Hack<T> : HackBase where T : Hack<T>
    {
        /** A reference to this class's instance. */
        static T instance;

        /** Maps the method being patched to the method doing said patching. */
        static Dictionary<MethodBase, MethodInfo> patchmap = new Dictionary<MethodBase, MethodInfo>();

        /** A stack to allow patches to trigger additional patches. 
         * This is necessary when dealing with delegates. */
        static Stack<MethodBase> to_be_patched = new Stack<MethodBase>();

        /** Applies the methods annotated with BytecodePatch defined in this class. */
        public override void Entry(IModHelper helper) {
            if (typeof(T) != this.GetType()) throw new Exception($"The type of this ({this.GetType()}) must be the same as the generic argument T ({typeof(T)}).");
            base.Entry(helper);
            instance = (T)this;

            // Iterate all methods in this class and search for those that have a BytecodePatch annotation.
            var methods = typeof(T).GetMethods(AccessTools.all);
            var apply = AccessTools.Method(typeof(Hack<T>), "ApplyPatch");
            foreach (MethodInfo patch in methods) {
                var bytecode_patches = patch.GetCustomAttributes<BytecodePatch>();
                foreach (var bp in bytecode_patches) {
                    if (bp.IsEnabled(this)) {
                        // Add the patch to the to_be_patched stack.
                        ChainPatch(bp.GetMethod(), patch);
                    }
                }
                // Apply the patch to the method specified in the annotation.
                while (to_be_patched.Count > 0) {
                    var method = to_be_patched.Pop();
                    harmony.Patch(method, null, null, new HarmonyMethod(apply));
                }
            }
        }

        /** Applies the given patch to the given method. 
         * This method can be called from within a patch method, for example to patch delegate functions. */
        public void ChainPatch(MethodInfo method, MethodInfo patch) {
            if (patchmap.ContainsKey(method)) {
                throw new Exception($"Can't apply patch {patch} to {method}, because it is already patched by {patchmap[method]}.");
            }
            patchmap[method] = patch;
            to_be_patched.Push(method);
        }

        /** Called by harmony to apply a patch. */ 
        private static IEnumerable<CodeInstruction> ApplyPatch(MethodBase original, ILGenerator generator, IEnumerable<CodeInstruction> instructions) {
            // Set the patch's references to this method's arguments.
            instance.original = original;
            instance.generator = generator;
            instance.instructions = new List<CodeInstruction>(instructions);

            // Obtain the patch method
            var patch = patchmap[original];

            // Print info 
            string info = $"Applying patch {patch.Name} to {original} in {original.DeclaringType.FullName}.";
            instance.Monitor.Log(info, LogLevel.Trace);

            // Apply the patch
            patch.Invoke(instance, null);

            // Keep a reference to the resulting code.
            instructions = instance.instructions;

            // Clear the patch's references to this method's arguments.
            instance.original = null;
            instance.generator = null;
            instance.instructions = null;

            // Return the resulting code.
            return instructions;
        }
    }

    public abstract class HackWithConfig<T, C> : Hack<T> where T : HackWithConfig<T, C> where C : class, new()
    {
        public C config;

        public override void Entry(IModHelper helper) {
            config = helper.ReadConfig<C>();
            base.Entry(helper);
        }
    }
}
