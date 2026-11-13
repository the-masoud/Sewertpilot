using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // One question the installed vendor asked while the single authorised persistence attempt was
    // running, together with the exact decision this adapter returned. Only hashes of the vendor
    // message are recorded: the message text itself is never copied into evidence.
    internal sealed class P16SaveQuestionRecord
    {
        public int Index { get; set; }
        public string Classification { get; set; }
        public string Decision { get; set; }
        public string MessageSha256 { get; set; }
        public string CaptionSha256 { get; set; }
        public int MessageLength { get; set; }
        public string EmbeddedTargetPath { get; set; }
        public bool DefaultYesRequested { get; set; }
    }

    // Pure classification policy for every question the vendor can ask during persistence.
    //
    // Verified vendor text (see contract-inspection.txt in this mission's evidence directory):
    //   Haestad.Framework 24.0.2.0 resource Prompt_SaveNewerSchema_Text
    //     "Save file over older version?"
    //   Haestad.Framework.Windows.Forms 24.0.2.0 resource hfwff.SaveNewerSchemaPrompt
    //     "Files saved with this version of %1 cannot be read by older versions.%n%n
    //      Would you like to overwrite \"%2\"?"
    //
    // Only the schema-upgrade/overwrite question may ever be answered Yes, and only when the target it
    // names (or, for the path-free form, the project instance it was bound to) is the exact authorised
    // disposable working model. Everything else is answered Cancel. There is no fallthrough that
    // answers a question affirmatively and no generic "answer everything" mode.
    internal static class P16SaveQuestionPolicy
    {
        internal const string SchemaOverwriteAuthorized = "SCHEMA_OVERWRITE_AUTHORIZED";
        internal const string SchemaOverwriteUnauthorizedTarget = "SCHEMA_OVERWRITE_UNAUTHORIZED_TARGET";
        internal const string SchemaOverwriteTargetNotBound = "SCHEMA_OVERWRITE_TARGET_NOT_BOUND";
        internal const string UnrecognizedQuestion = "UNRECOGNIZED_QUESTION";
        internal const string QuestionBudgetExceeded = "QUESTION_BUDGET_EXCEEDED";
        internal const string MalformedQuestion = "MALFORMED_QUESTION";
        internal const string DatabaseCompactionDeclined = "DATABASE_COMPACTION_DECLINED";
        internal const string DatabaseCompactionTargetNotBound = "DATABASE_COMPACTION_TARGET_NOT_BOUND";

        internal const string DecisionYes = "Yes";
        internal const string DecisionNo = "No";
        internal const string DecisionCancel = "Cancel";

        // Bounds so a misbehaving vendor surface cannot grow the evidence without limit.
        internal const int MaxMessageLength = 4096;
        internal const int MaxQuestions = 16;
        internal const int MaxRecordedPathLength = 512;

        internal const string VerifiedSchemaPromptText = "Save file over older version?";
        internal const string VerifiedSchemaPromptFragment = "cannot be read by older versions";
        internal const string VerifiedCompactPromptCaption = "Compact Database";
        internal const string VerifiedCompactPromptFragment = "The application is about to compact database, do you want to proceed?";
        internal const string VerifiedCompactPromptNoteFragment = "Tools->Options and configuring the \"Compact database after\" option.";
        private const string VerifiedSchemaPromptTargetPrefix = "overwrite";

        // Whitespace/line-break normalisation. The vendor templates use %n placeholders, so the same
        // question can arrive with CRLF, LF or %n separators.
        internal static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder(text.Length);
            bool pendingSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '%' && i + 1 < text.Length && (text[i + 1] == 'n' || text[i + 1] == 'N'))
                {
                    pendingSpace = true; i++; continue;
                }
                if (char.IsWhiteSpace(c)) { pendingSpace = true; continue; }
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                pendingSpace = false;
                builder.Append(c);
            }
            return builder.ToString().Trim();
        }

        // Extracts the quoted target path from the modal template form:
        //   ... Would you like to overwrite "%2"?   ->   %2
        // Returns null when the message carries no quoted target.
        internal static string EmbeddedTarget(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return null;
            int marker = normalized.IndexOf(VerifiedSchemaPromptTargetPrefix, StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return null;
            int open = normalized.IndexOf('"', marker);
            if (open < 0) return null;
            int close = normalized.IndexOf('"', open + 1);
            if (close <= open + 1) return null;
            string value = normalized.Substring(open + 1, close - open - 1).Trim();
            if (value.Length == 0 || value.Length > MaxRecordedPathLength) return null;
            return value;
        }

        internal static bool SamePath(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(left).TrimEnd('\\'), Path.GetFullPath(right).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        // Compatibility overload used by save-policy tests that do not provide a caption.
        internal static string Classify(string message, string authorizedModelPath, out string embeddedTarget)
        {
            return Classify(message, null, authorizedModelPath, out embeddedTarget);
        }

        // Only two exact vendor questions are handled: schema overwrite (Yes, bound to the working copy)
        // and database compaction (No, bound to the working copy). Unknown prompts are Cancelled.
        internal static string Classify(string message, string caption, string authorizedModelPath, out string embeddedTarget)
        {
            embeddedTarget = null;
            if (message == null || message.Length == 0 || message.Length > MaxMessageLength) return MalformedQuestion;
            string normalized = Normalize(message);
            if (normalized.Length == 0) return MalformedQuestion;

            if (string.Equals(normalized, VerifiedCompactPromptFragment, StringComparison.OrdinalIgnoreCase) ||
                (normalized.IndexOf(VerifiedCompactPromptFragment, StringComparison.OrdinalIgnoreCase) >= 0 &&
                 normalized.IndexOf(VerifiedCompactPromptNoteFragment, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                if (!string.Equals(Normalize(caption), VerifiedCompactPromptCaption, StringComparison.OrdinalIgnoreCase))
                    return UnrecognizedQuestion;
                if (string.IsNullOrEmpty(authorizedModelPath)) return DatabaseCompactionTargetNotBound;
                return DatabaseCompactionDeclined;
            }

            // Path-free verified schema form. The handler must be bound to this disposable project.
            if (string.Equals(normalized, VerifiedSchemaPromptText, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(authorizedModelPath) ? SchemaOverwriteTargetNotBound : SchemaOverwriteAuthorized;

            // Schema template form. The embedded target must be the authorised working model.
            if (normalized.IndexOf(VerifiedSchemaPromptFragment, StringComparison.OrdinalIgnoreCase) < 0)
                return UnrecognizedQuestion;
            embeddedTarget = EmbeddedTarget(normalized);
            if (embeddedTarget == null) return SchemaOverwriteTargetNotBound;
            if (string.IsNullOrEmpty(authorizedModelPath)) return SchemaOverwriteTargetNotBound;
            return SamePath(embeddedTarget, authorizedModelPath) ? SchemaOverwriteAuthorized : SchemaOverwriteUnauthorizedTarget;
        }

        internal static bool IsHandledClassification(string classification)
        {
            return string.Equals(classification, SchemaOverwriteAuthorized, StringComparison.Ordinal) ||
                string.Equals(classification, DatabaseCompactionDeclined, StringComparison.Ordinal);
        }

        internal static string DecisionFor(string classification)
        {
            if (string.Equals(classification, SchemaOverwriteAuthorized, StringComparison.Ordinal)) return DecisionYes;
            if (string.Equals(classification, DatabaseCompactionDeclined, StringComparison.Ordinal)) return DecisionNo;
            return DecisionCancel;
        }

        internal static string Sha256(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(new UTF8Encoding(false).GetBytes(value))).Replace("-", "");
        }
    }

    // Records every question the vendor asks during the one authorised persistence attempt and applies
    // the policy. A recorder is bound to exactly one save of exactly one verified project instance.
    internal sealed class P16SaveQuestionRecorder
    {
        private readonly List<P16SaveQuestionRecord> _records = new List<P16SaveQuestionRecord>();
        private readonly List<string> _warningDigests = new List<string>();
        private readonly object _gate = new object();
        private readonly string _authorizedModelPath;
        private int _warningCount;
        private bool _warningBudgetExceeded;

        internal P16SaveQuestionRecorder(string authorizedModelPath)
        {
            _authorizedModelPath = authorizedModelPath;
        }

        internal int Count { get { lock (_gate) return _records.Count; } }

        internal bool BudgetExceeded { get { lock (_gate) return _records.Any(r => r.Classification == P16SaveQuestionPolicy.QuestionBudgetExceeded); } }

        // True when any question other than the authorised schema-overwrite prompt was seen, or when the
        // question budget was exhausted. Any such attempt is treated as unclean even if the vendor
        // itself would have continued past the returned value.
        internal bool AnyUnauthorizedQuestion
        {
            get
            {
                lock (_gate)
                    return _records.Any(r => !P16SaveQuestionPolicy.IsHandledClassification(r.Classification));
            }
        }

        internal P16SaveQuestionRecord[] Records { get { lock (_gate) return _records.ToArray(); } }

        internal int WarningCount { get { lock (_gate) return _warningCount; } }

        internal string[] WarningDigests { get { lock (_gate) return _warningDigests.ToArray(); } }

        internal bool WarningBudgetExceeded { get { lock (_gate) return _warningBudgetExceeded; } }

        // IMessageHandler.ShowWarning is a vendor notification, not a question. Record only a digest
        // and return silently; never create UI or alter the question-decision policy. Storage is bounded.
        internal void RecordWarning(string message)
        {
            lock (_gate)
            {
                if (_warningCount < P16SaveQuestionPolicy.MaxQuestions)
                {
                    _warningDigests.Add(P16SaveQuestionPolicy.Sha256(message) ?? "EMPTY_MESSAGE");
                    _warningCount++;
                }
                else
                {
                    _warningBudgetExceeded = true;
                    _warningCount = P16SaveQuestionPolicy.MaxQuestions + 1;
                    if (!_warningDigests.Contains("WARNING_BUDGET_EXCEEDED"))
                        _warningDigests.Add("WARNING_BUDGET_EXCEEDED");
                }
            }
        }

        internal string[] Classifications { get { lock (_gate) return _records.Select(r => r.Classification).ToArray(); } }

        internal string[] Decisions { get { lock (_gate) return _records.Select(r => r.Decision).ToArray(); } }

        internal string Answer(string message, string caption, bool defaultYes)
        {
            lock (_gate)
            {
                if (_records.Count >= P16SaveQuestionPolicy.MaxQuestions)
                {
                    // The budget is already recorded; refuse everything after it.
                    var exhausted = new P16SaveQuestionRecord
                    {
                        Index = _records.Count,
                        Classification = P16SaveQuestionPolicy.QuestionBudgetExceeded,
                        Decision = P16SaveQuestionPolicy.DecisionCancel,
                        MessageSha256 = P16SaveQuestionPolicy.Sha256(message),
                        CaptionSha256 = P16SaveQuestionPolicy.Sha256(caption),
                        MessageLength = message == null ? 0 : message.Length,
                        DefaultYesRequested = defaultYes
                    };
                    if (!_records.Any(r => r.Classification == P16SaveQuestionPolicy.QuestionBudgetExceeded)) _records.Add(exhausted);
                    return P16SaveQuestionPolicy.DecisionCancel;
                }

                string embedded;
                string classification = P16SaveQuestionPolicy.Classify(message, caption, _authorizedModelPath, out embedded);
                string decision = P16SaveQuestionPolicy.DecisionFor(classification);
                _records.Add(new P16SaveQuestionRecord
                {
                    Index = _records.Count,
                    Classification = classification,
                    Decision = decision,
                    MessageSha256 = P16SaveQuestionPolicy.Sha256(message),
                    CaptionSha256 = P16SaveQuestionPolicy.Sha256(caption),
                    MessageLength = message == null ? 0 : message.Length,
                    EmbeddedTargetPath = embedded,
                    DefaultYesRequested = defaultYes
                });
                return decision;
            }
        }
    }

    // Runtime implementation of IMessageQuestionHandler and its complete inherited interface closure.
    // Every public instance method declared by the requested interface or any base interface is emitted.
    // The installed contract is intentionally fail-closed: only ShowQuestion(string,string,bool) and
    // IMessageHandler.ShowWarning(string) are accepted. A vendor contract change fails before persistence.
    internal static class P16SaveQuestionHandler
    {
        private const string CallbackFieldName = "Callback";
        private const string WarningCallbackFieldName = "WarningCallback";
        private const string ShowQuestionMember = "ShowQuestion";
        private const string ShowWarningMember = "ShowWarning";

        private sealed class InterfaceMethodGroup
        {
            internal MethodInfo Method;
            internal MethodInfo[] Overrides;
        }

        private static string MethodKey(MethodInfo method)
        {
            return method.Name + "|" + method.ReturnType.AssemblyQualifiedName + "|" +
                string.Join("|", method.GetParameters().Select(p => p.ParameterType.AssemblyQualifiedName));
        }

        private static bool IsQuestionMethod(MethodInfo method, Type resultEnum)
        {
            ParameterInfo[] p = method.GetParameters();
            return method.Name == ShowQuestionMember && method.ReturnType == resultEnum && p.Length == 3 &&
                p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(string) &&
                p[2].ParameterType == typeof(bool);
        }

        private static bool IsWarningMethod(MethodInfo method)
        {
            ParameterInfo[] p = method.GetParameters();
            return method.Name == ShowWarningMember && method.ReturnType == typeof(void) &&
                p.Length == 1 && p[0].ParameterType == typeof(string);
        }

        internal static object Create(Type questionHandlerContract, Type resultEnum, P16SaveQuestionRecorder recorder)
        {
            if (questionHandlerContract == null || !questionHandlerContract.IsInterface)
                throw new InvalidOperationException("CRITICAL_VENDOR_REFLECTION_FAILURE: the message-question contract is unavailable.");
            if (resultEnum == null || !resultEnum.IsEnum)
                throw new InvalidOperationException("CRITICAL_VENDOR_REFLECTION_FAILURE: the message-handler result enum is unavailable.");
            if (recorder == null) throw new ArgumentNullException(nameof(recorder));

            MethodInfo questionMethod = questionHandlerContract.GetMethod(ShowQuestionMember,
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(string), typeof(bool) }, null);
            if (questionMethod == null || questionMethod.ReturnType != resultEnum)
                throw new InvalidOperationException("CRITICAL_VENDOR_REFLECTION_FAILURE: " + questionHandlerContract.FullName +
                    "." + ShowQuestionMember + "(string, string, bool) is unavailable.");
            if (!Enum.IsDefined(resultEnum, P16SaveQuestionPolicy.DecisionYes) ||
                !Enum.IsDefined(resultEnum, P16SaveQuestionPolicy.DecisionNo) ||
                !Enum.IsDefined(resultEnum, P16SaveQuestionPolicy.DecisionCancel))
                throw new InvalidOperationException("CRITICAL_VENDOR_REFLECTION_FAILURE: the message-handler result enum lacks Yes/No/Cancel.");

            // Type.GetMethods() on an interface omits inherited methods on .NET Framework. Explicitly
            // traverse the full interface closure, then group identical signatures so one implementation
            // can satisfy a shared base member while DefineMethodOverride records every declaration.
            Type[] interfaces = questionHandlerContract.GetInterfaces().Concat(new[] { questionHandlerContract }).Distinct().ToArray();
            InterfaceMethodGroup[] methods = interfaces
                .SelectMany(i => i.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                .GroupBy(MethodKey, StringComparer.Ordinal)
                .Select(g => new InterfaceMethodGroup { Method = g.First(), Overrides = g.ToArray() })
                .ToArray();

            int questionMethodCount = methods.Count(g => IsQuestionMethod(g.Method, resultEnum));
            int warningMethodCount = methods.Count(g => IsWarningMethod(g.Method));
            if (questionMethodCount != 1 || warningMethodCount != 1 ||
                methods.Any(g => !IsQuestionMethod(g.Method, resultEnum) && !IsWarningMethod(g.Method)))
                throw new InvalidOperationException("CRITICAL_VENDOR_REFLECTION_FAILURE: unsupported or incomplete inherited message-handler interface closure.");

            int yesValue = Convert.ToInt32(Enum.Parse(resultEnum, P16SaveQuestionPolicy.DecisionYes), CultureInfo.InvariantCulture);
            int noValue = Convert.ToInt32(Enum.Parse(resultEnum, P16SaveQuestionPolicy.DecisionNo), CultureInfo.InvariantCulture);
            int cancelValue = Convert.ToInt32(Enum.Parse(resultEnum, P16SaveQuestionPolicy.DecisionCancel), CultureInfo.InvariantCulture);
            var callbackType = typeof(Func<string, string, bool, int>);
            var warningCallbackType = typeof(Action<string>);

            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                new AssemblyName("SewerPilot.P16SaveQuestionHandler"), AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("P16SaveQuestionHandler");
            TypeBuilder type = module.DefineType("SewerPilot.P16SaveQuestionHandler.Runtime",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                typeof(object), new[] { questionHandlerContract });

            FieldBuilder callback = type.DefineField(CallbackFieldName, callbackType, FieldAttributes.Public | FieldAttributes.Static);
            FieldBuilder warningCallback = type.DefineField(WarningCallbackFieldName, warningCallbackType, FieldAttributes.Public | FieldAttributes.Static);

            foreach (InterfaceMethodGroup group in methods)
            {
                MethodInfo contractMethod = group.Method;
                ParameterInfo[] parameters = contractMethod.GetParameters();
                Type[] parameterTypes = parameters.Select(p => p.ParameterType).ToArray();
                MethodBuilder emittedMethod = type.DefineMethod(contractMethod.Name,
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
                    MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                    contractMethod.ReturnType, parameterTypes);

                ILGenerator il = emittedMethod.GetILGenerator();
                if (IsQuestionMethod(contractMethod, resultEnum))
                {
                    il.Emit(OpCodes.Ldsfld, callback);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Ldarg_3);
                    il.Emit(OpCodes.Callvirt, callbackType.GetMethod("Invoke"));
                    il.Emit(OpCodes.Box, typeof(int));
                    il.Emit(OpCodes.Unbox_Any, resultEnum);
                    il.Emit(OpCodes.Ret);
                }
                else
                {
                    // ShowWarning is intentionally a silent no-op with a bounded SHA-256 audit record.
                    il.Emit(OpCodes.Ldsfld, warningCallback);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Callvirt, warningCallbackType.GetMethod("Invoke"));
                    il.Emit(OpCodes.Ret);
                }

                foreach (MethodInfo contractDeclaration in group.Overrides)
                    type.DefineMethodOverride(emittedMethod, contractDeclaration);
            }

            Type emitted = type.CreateType();
            object instance = Activator.CreateInstance(emitted);
            if (!interfaces.All(i => i.IsInstanceOfType(instance)))
                throw new InvalidOperationException("CRITICAL_VENDOR_REFLECTION_FAILURE: the runtime handler does not implement the complete interface closure.");

            var dispatch = new Func<string, string, bool, int>((message, caption, defaultYes) =>
            {
                string decision = recorder.Answer(message, caption, defaultYes);
                if (string.Equals(decision, P16SaveQuestionPolicy.DecisionYes, StringComparison.Ordinal)) return yesValue;
                if (string.Equals(decision, P16SaveQuestionPolicy.DecisionNo, StringComparison.Ordinal)) return noValue;
                return cancelValue;
            });
            var warningDispatch = new Action<string>(message => recorder.RecordWarning(message));
            FieldInfo callbackField = emitted.GetField(CallbackFieldName, BindingFlags.Public | BindingFlags.Static);
            FieldInfo warningCallbackField = emitted.GetField(WarningCallbackFieldName, BindingFlags.Public | BindingFlags.Static);
            if (callbackField == null || warningCallbackField == null)
                throw new InvalidOperationException("CRITICAL_VENDOR_REFLECTION_FAILURE: a runtime handler callback field is unavailable.");
            AdapterWriteBinding.InvokeSetValue(callbackField, null, dispatch);
            AdapterWriteBinding.InvokeSetValue(warningCallbackField, null, warningDispatch);
            return instance;
        }
    }

    // Pure save-result classification. The verified non-UI persistence member returns void, so the
    // outcome is never read from a return value. Persistence is established from the output inventory
    // delta of the disposable working copy; the reopened readback session is the authority for a pass.
    internal static class P16ProjectPersistenceResult
    {
        internal const string Persisted = "PERSISTED";
        internal const string NotPersisted = "NOT_PERSISTED";
        internal const string Failed = "FAILED";
        internal const string UncleanQuestionSeen = "UNCLEAN_QUESTION_SEEN";

        internal static string Classify(bool exceptionRaised, bool anyUnauthorizedQuestion, int changedFileCount, bool? projectDirtyAfter)
        {
            if (exceptionRaised) return Failed;
            if (anyUnauthorizedQuestion) return UncleanQuestionSeen;
            if (changedFileCount <= 0) return NotPersisted;
            // A vendor save that wrote files but still reports the project dirty is internally
            // inconsistent; it is not accepted as persistence.
            if (projectDirtyAfter == true) return NotPersisted;
            return Persisted;
        }

        internal static bool IsPersisted(string classification) =>
            string.Equals(classification, Persisted, StringComparison.Ordinal);
    }
}
