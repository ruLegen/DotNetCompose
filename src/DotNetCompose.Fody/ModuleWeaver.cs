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
    public class ModuleWeaver : BaseModuleWeaver
    {
        private const string AttributeFullName = "DotNetCompose.Runtime.ComposableAttribute";
        private const string ComposableContentActionFullName = "DotNetCompose.Runtime.ComposableAction";

        public override IEnumerable<string> GetAssembliesForScanning()
        {
            yield return "mscorlib";
            yield return "System";
            yield return "System.Runtime";
            yield return "netstandard";
        }

        public override void Execute()
        {
            IEnumerable<MethodDefinition> methods = GetMatchingTypes();

            Log("================================");
            foreach (var type in methods.ToList())
            {
                ProcessMethod(type);
            }
        }

        private void ProcessMethod(MethodDefinition method)
        {
            Log("{0}", method);
            foreach (var param in method.Parameters)
            {
                Log("\tParam {0} - {1} - Generic={2}", param.ParameterType.FullName, param.Name, param.ParameterType.IsGenericParameter);
            }

            MethodBody methodBody = method.Body;
            var instructions = methodBody.Instructions;
            var m = new MethodDefinition(method.Name + "Gen",
                MethodAttributes.Public | MethodAttributes.Static,
                method.ReturnType);

            foreach(var p in method.Parameters)
            {
                m.Parameters.Add(p);
            }
            foreach (var p in method.GenericParameters)
            {
                var gp = new GenericParameter(p.Name, m);
                m.GenericParameters.Add(gp);
            }
            var bIlProc = m.Body.GetILProcessor();
            foreach (var item in instructions)
            {
                bIlProc.Append(item.GetPrototype());
                if (item.OpCode != OpCodes.Call)
                    continue;

                if (!(item.Operand is MethodDefinition) && !(item.Operand is GenericInstanceMethod))
                    continue;

                bool isComposable = false;
                if (item.Operand is MethodDefinition methodDef)
                {
                    isComposable = IsComposable(methodDef.CustomAttributes);
                }
                else if (item.Operand is GenericInstanceMethod genericInstanceMethod)
                {
                    isComposable = IsComposable(genericInstanceMethod.ElementMethod.Resolve().CustomAttributes);
                }

                Log("{0} - {1} {2} {3}", item.OpCode, item.Operand, item.Operand?.GetType(), isComposable);
            }

            method.DeclaringType.Methods.Add(m);
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
