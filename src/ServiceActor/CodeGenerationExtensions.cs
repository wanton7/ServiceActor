﻿using Microsoft.CSharp;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ServiceActor
{
    public static class CodeGenerationExtensions
    {
        public static string GetTypeReferenceCode(this Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return GenerateReferenceCodeForTypeString(type.ToString().Replace('+', '.'));
        }

        public static string GetTypeReferenceCode(this ParameterInfo parameterInfo)
        {
            if (parameterInfo == null)
            {
                throw new ArgumentNullException(nameof(parameterInfo));
            }

            return GenerateReferenceCodeForTypeString(parameterInfo.ParameterType.ToString().Replace('+', '.'), parameterInfo.IsOut);
        }

        public static string GetMethodDeclarationCode(this MethodInfo methodInfo)
        {
            if (methodInfo == null)
            {
                throw new ArgumentNullException(nameof(methodInfo));
            }

            if (!methodInfo.IsGenericMethod)
                return $"{methodInfo.ReturnType.GetTypeReferenceCode()} {methodInfo.Name}({string.Join(", ", methodInfo.GetParameters().Select(_ => _.GetTypeReferenceCode() + " " + _.Name))})";

            return $"{methodInfo.ReturnType.GetTypeReferenceCode()} {methodInfo.Name}<{string.Join(", ", methodInfo.GetGenericArguments().Select(_=>_.GetTypeReferenceCode()))}>({string.Join(", ", methodInfo.GetParameters().Select(_ => _.GetTypeReferenceCode() + " " + _.Name))})";
        }

        public static string GetMethodInvocationCode(this MethodInfo methodInfo)
        {
            if (methodInfo == null)
            {
                throw new ArgumentNullException(nameof(methodInfo));
            }

            if (!methodInfo.IsGenericMethod)
                return $"{methodInfo.Name}({string.Join(", ", methodInfo.GetParameters().Select(_ => _.Name))})";

            return $"{methodInfo.Name}<{string.Join(", ", methodInfo.GetGenericArguments().Select(_ => _.GetTypeReferenceCode()))}>({string.Join(", ", methodInfo.GetParameters().Select(_ => _.Name))})";
        }

        private static string GenerateReferenceCodeForTypeString(string typeString, bool isOut = false)
        {
            var generatedCodeTokens = typeString.Split('`');

            if (generatedCodeTokens.Length == 1)
            {
                if (generatedCodeTokens[0].EndsWith("&"))
                    return (isOut ? "out " : "ref ") + generatedCodeTokens[0].TrimEnd('&');

                return generatedCodeTokens[0] == "System.Void" ? "void" : generatedCodeTokens[0];
            }

            var generatedCodeGenricTagStartIndex = typeString.IndexOf('[');
            var generatedCodeGenricTagEndIndex = typeString.LastIndexOf(']');
            if (generatedCodeGenricTagStartIndex > -1 && generatedCodeGenricTagEndIndex > -1)
            {
                var genericTypeDefinitionArguments = typeString.Substring(generatedCodeGenricTagStartIndex + 1, generatedCodeGenricTagEndIndex - generatedCodeGenricTagStartIndex - 1);
                var genericTypeDefinitionArgumentsTokens = genericTypeDefinitionArguments.Split(',');
                var refParameter = typeString.EndsWith("&");
                return $"{(refParameter ? (isOut ? "out " : "ref ") : string.Empty)}{generatedCodeTokens[0]}<{string.Join(", ", genericTypeDefinitionArgumentsTokens.Select(_ => GenerateReferenceCodeForTypeString(_)))}>";
            }

            throw new NotSupportedException();
        }

        
    }
}
