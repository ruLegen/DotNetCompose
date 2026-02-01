using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DotNetCompose.Runtime
{
    class ModuleWeaverContext
    {
        public MethodReference ComposableLambdaWrapperImplicitConvertionFromComposableAction { get; set; }
    }
    public class ModuleWeaver : BaseModuleWeaver
    {
        private const string AttributeFullName = "DotNetCompose.Runtime.ComposableAttribute";
        private const string ComposableContentActionFullName = "DotNetCompose.Runtime.ComposableAction";

        public override IEnumerable<string> GetAssembliesForScanning()
        {
            yield return "DotNetCompose.Runtime";
            yield return "mscorlib";
            yield return "System";
            yield return "System.Runtime";
            yield return "netstandard";
        }

        public override void Execute()
        {
#if DEBUG
            if (!Debugger.IsAttached)
            {
               // Debugger.Launch();
            }
#endif
            TypeDefinition labmdaWrapperType = FindTypeDefinition("DotNetCompose.Runtime.ComposableLambdaWrapper");
            string implicitOperatorMethodName = string.Format("op_Implicit({0})", ComposableContentActionFullName);
            MethodDefinition implicitConvertionOperator = labmdaWrapperType.Methods.FirstOrDefault(m => m.FullName.Contains(implicitOperatorMethodName));
            MethodReference reference = ModuleDefinition.ImportReference(implicitConvertionOperator);
            ModuleWeaverContext ctx = new ModuleWeaverContext()
            {
                ComposableLambdaWrapperImplicitConvertionFromComposableAction = reference,
            };

            IEnumerable<MethodDefinition> methods = GetMatchingTypes();
            foreach (MethodDefinition type in methods.ToList())
            {
                ProcessMethod(ctx, type);
            }
        }

        private void ProcessMethod(ModuleWeaverContext ctx, MethodDefinition method)
        {

            MethodBody methodBody = method.Body;
            method.Body = new MethodBody(method);
            var processor = method.Body.GetILProcessor();

            processor.Emit(OpCodes.Nop);
            int paramStartIndex = method.IsStatic ? 0 : 1;


            // Load 'this' for instance methods
            if (!method.IsStatic)
            {
                processor.Emit(OpCodes.Ldarg_0);
            }

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                int argIndex = i + paramStartIndex;

                switch (argIndex)
                {
                    case 0:
                        processor.Emit(OpCodes.Ldarg_0);
                        ConvertArgumentIfComposableAction(ctx, processor, method, argIndex);
                        break;
                    case 1:
                        processor.Emit(OpCodes.Ldarg_1);
                        ConvertArgumentIfComposableAction(ctx, processor, method, argIndex);
                        break;
                    case 2:
                        processor.Emit(OpCodes.Ldarg_2);
                        ConvertArgumentIfComposableAction(ctx, processor, method, argIndex);
                        break;
                    case 3:
                        processor.Emit(OpCodes.Ldarg_3);
                        ConvertArgumentIfComposableAction(ctx, processor, method, argIndex);
                        break;
                    default:
                        processor.Emit(OpCodes.Ldarg_S, method.Parameters[i]);
                        ConvertArgumentIfComposableAction(ctx, processor, method, argIndex);
                        break;
                }
            }

            // Check if method already exists
            var existingMethod = method.DeclaringType.Methods.FirstOrDefault(m => m.Name == method.Name + "_Generated");
            // Call the generated method
            processor.Emit(OpCodes.Call, existingMethod);

            // Return
            processor.Emit(OpCodes.Ret);
        }

        private void ConvertArgumentIfComposableAction(ModuleWeaverContext ctx, ILProcessor processor, MethodDefinition method, int argIndex)
        {
            if (!method.IsStatic && argIndex == 0)
                return;

            ParameterDefinition arg = method.Parameters[argIndex];
            bool isComposable = arg.ParameterType.FullName == ComposableContentActionFullName;
            if (!isComposable)
                return;
            processor.Emit(OpCodes.Call, ctx.ComposableLambdaWrapperImplicitConvertionFromComposableAction);
        }

        public IEnumerable<MethodDefinition> GetMatchingTypes()
        {
            return ModuleDefinition.GetTypes()
                .SelectMany(_ => _.Methods)
                .Where(_ => IsComposable(_.CustomAttributes))
                .Where(_ => _.IsStatic);
        }

        private bool IsComposable(Collection<CustomAttribute> customAttributes)
        {
            return customAttributes.Any(a => a.AttributeType.FullName == AttributeFullName);
        }

        private void Log<T>(T obj)
        {
            WriteMessage(obj?.ToString(), MessageImportance.High);
        }
        private void Log(string message, params object[] args)
        {
            if (args?.Length == 0)
                WriteMessage(message, MessageImportance.High);
            else
                WriteMessage(string.Format(message, args), MessageImportance.High);
        }

    }
}
