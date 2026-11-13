using System;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Mirrors the installed Haestad.Support.User.MessageHandlerResult literal values
    // (OK=0, Yes=1, No=2, Cancel=3) so the runtime proxy mechanism can be qualified without a vendor
    // reference. These are probe types only; they are never passed to a vendor API.
    public enum P16SavePromptProbeResult { OK = 0, Yes = 1, No = 2, Cancel = 3 }

    // Mirrors the installed two-interface closure: IMessageQuestionHandler inherits IMessageHandler.
    public interface P16SavePromptProbeMessageHandler
    {
        void ShowWarning(string message);
    }

    public interface P16SavePromptProbeContract : P16SavePromptProbeMessageHandler
    {
        P16SavePromptProbeResult ShowQuestion(string message, string caption, bool defaultYes);
    }

    internal static partial class Program
    {
        private const string P16SavePromptAuthorizedModel =
            @"C:\Users\Public\AppData\Local\Temp\SewerPilot\OpenFlows\Working\0123456789abcdef0123456789abcdef\Azadegab ssw 26.10.25.stsw";

        private const string P16SavePromptForeignModel =
            @"C:\Users\Public\AppData\Local\Temp\SewerPilot\OpenFlows\Working\fedcba9876543210fedcba9876543210\Other Model.stsw";

        private static string P16SavePromptModalTemplate(string modelPath)
        {
            // Verbatim shape of Haestad.Framework.Windows.Forms resource hfwff.SaveNewerSchemaPrompt.
            return "Files saved with this version of SewerGEMS cannot be read by older versions.%n%nWould you like to overwrite \"" +
                modelPath + "\"?";
        }

        private static string P16SavePromptClassify(string message, string authorized, out string embedded)
        {
            return P16SaveQuestionPolicy.Classify(message, authorized, out embedded);
        }

        // Only the verified schema-upgrade/overwrite prompt may ever be answered Yes, and only when it
        // targets the exact authorised disposable working model.
        private static void P16SavePrompt_OnlyVerifiedSchemaPromptIsAuthorized()
        {
            string embedded;

            // 1. The path-free verified resource text (Haestad.Framework Prompt_SaveNewerSchema_Text).
            Check(P16SavePromptClassify(P16SaveQuestionPolicy.VerifiedSchemaPromptText, P16SavePromptAuthorizedModel, out embedded)
                    == P16SaveQuestionPolicy.SchemaOverwriteAuthorized,
                "The verified path-free schema prompt is the authorised classification.");
            Check(embedded == null, "The path-free form carries no embedded target.");
            Check(P16SaveQuestionPolicy.DecisionFor(P16SaveQuestionPolicy.SchemaOverwriteAuthorized) == P16SaveQuestionPolicy.DecisionYes,
                "The authorised classification maps to Yes.");

            // 2. The modal template naming the authorised working model.
            Check(P16SavePromptClassify(P16SavePromptModalTemplate(P16SavePromptAuthorizedModel), P16SavePromptAuthorizedModel, out embedded)
                    == P16SaveQuestionPolicy.SchemaOverwriteAuthorized,
                "The modal schema prompt naming the authorised working model is authorised.");
            Check(embedded == P16SavePromptAuthorizedModel, "The embedded target is extracted verbatim.");

            // 3. The same modal prompt naming a different model is refused.
            Check(P16SavePromptClassify(P16SavePromptModalTemplate(P16SavePromptForeignModel), P16SavePromptAuthorizedModel, out embedded)
                    == P16SaveQuestionPolicy.SchemaOverwriteUnauthorizedTarget,
                "A schema prompt naming any other model is refused.");
            Check(P16SaveQuestionPolicy.DecisionFor(P16SaveQuestionPolicy.SchemaOverwriteUnauthorizedTarget) == P16SaveQuestionPolicy.DecisionCancel,
                "An unauthorised target maps to Cancel.");

            // 4. Every other verified vendor question is unrecognised and therefore refused.
            foreach (string other in new[]
            {
                // Haestad.Framework resource overwriteReadOnly
                "An existing read-only hydraulic model file would be overwritten. Save anyway?",
                // Haestad.Framework resource userPromptForFileLock (path-prefixed form)
                P16SavePromptAuthorizedModel + " has been modified by another user since you opened it.  We recommend you do a Save As instead.%n%nSaving will overwrite the file. Do you want to continue?",
                "An existing read-only hydraulic model file would be overwritten. Save anyway?%n%nWould you like to overwrite \"" + P16SavePromptAuthorizedModel + "\"?",
                "Retry the failing operation?",
                "Overwrite existing results?"
            })
            {
                Check(P16SavePromptClassify(other, P16SavePromptAuthorizedModel, out embedded) == P16SaveQuestionPolicy.UnrecognizedQuestion,
                    "An unrecognised question must be refused: " + other);
                Check(P16SaveQuestionPolicy.DecisionFor(P16SaveQuestionPolicy.UnrecognizedQuestion) == P16SaveQuestionPolicy.DecisionCancel,
                    "An unrecognised question maps to Cancel.");
            }

            // 5. Malformed input is refused rather than guessed at.
            Check(P16SavePromptClassify(null, P16SavePromptAuthorizedModel, out embedded) == P16SaveQuestionPolicy.MalformedQuestion,
                "A null question is malformed.");
            Check(P16SavePromptClassify("", P16SavePromptAuthorizedModel, out embedded) == P16SaveQuestionPolicy.MalformedQuestion,
                "An empty question is malformed.");
            Check(P16SavePromptClassify("   %n%n  ", P16SavePromptAuthorizedModel, out embedded) == P16SaveQuestionPolicy.MalformedQuestion,
                "A whitespace-only question is malformed.");
            Check(P16SavePromptClassify(new string('x', P16SaveQuestionPolicy.MaxMessageLength + 1), P16SavePromptAuthorizedModel, out embedded)
                    == P16SaveQuestionPolicy.MalformedQuestion,
                "An oversized question is malformed rather than truncated.");

            // 6. The schema prompt without a bound target is unanswerable.
            Check(P16SavePromptClassify(P16SaveQuestionPolicy.VerifiedSchemaPromptText, null, out embedded)
                    == P16SaveQuestionPolicy.SchemaOverwriteTargetNotBound,
                "The schema prompt without a bound working model is refused.");
            Check(P16SavePromptClassify(P16SavePromptModalTemplate(P16SavePromptAuthorizedModel), null, out embedded)
                    == P16SaveQuestionPolicy.SchemaOverwriteTargetNotBound,
                "The modal schema prompt without a bound working model is refused.");
        }

        // The runtime proxy answers the authorised question with the vendor's own Yes literal, answers
        // everything else with Cancel, and records every question it saw.
        private static void P16SavePrompt_DeclinesOnlyVerifiedDatabaseCompactionQuestion()
        {
            const string caption = "Compact Database";
            string message = P16SaveQuestionPolicy.VerifiedCompactPromptFragment +
                "\n\nNote:  You can control how frequently this message is displayed by going to Tools->Options and configuring the \"Compact database after\" option.";
            string embedded;
            string classification = P16SaveQuestionPolicy.Classify(message, caption, P16SavePromptAuthorizedModel, out embedded);
            Check(classification == P16SaveQuestionPolicy.DatabaseCompactionDeclined, "The exact vendor compaction prompt is recognized.");
            Check(P16SaveQuestionPolicy.DecisionFor(classification) == P16SaveQuestionPolicy.DecisionNo,
                "The verified compaction prompt is answered No, which skips compaction.");
            Check(P16SaveQuestionPolicy.Classify(message, "Other Question", P16SavePromptAuthorizedModel, out embedded) ==
                P16SaveQuestionPolicy.UnrecognizedQuestion, "A different caption is not accepted.");
            Check(P16SaveQuestionPolicy.Classify(message, caption, null, out embedded) ==
                P16SaveQuestionPolicy.DatabaseCompactionTargetNotBound, "The compaction response requires a bound disposable model.");

            var recorder = new P16SaveQuestionRecorder(P16SavePromptAuthorizedModel);
            var handler = (P16SavePromptProbeContract)P16SaveQuestionHandler.Create(
                typeof(P16SavePromptProbeContract), typeof(P16SavePromptProbeResult), recorder);
            Check(handler.ShowQuestion(message, caption, false) == P16SavePromptProbeResult.No,
                "The emitted runtime handler returns the vendor No enum value.");
            Check(recorder.Count == 1 && recorder.Classifications[0] == P16SaveQuestionPolicy.DatabaseCompactionDeclined,
                "The open-time compaction decision is recorded.");
            Check(recorder.Decisions[0] == P16SaveQuestionPolicy.DecisionNo && !recorder.AnyUnauthorizedQuestion,
                "The known non-mutating decision is accepted without authorizing unknown questions.");
        }

        private static void P16SavePrompt_HandlerAnswersOnlyAuthorizedAndRecordsEachQuestion()
        {
            var recorder = new P16SaveQuestionRecorder(P16SavePromptAuthorizedModel);
            object proxy = P16SaveQuestionHandler.Create(typeof(P16SavePromptProbeContract), typeof(P16SavePromptProbeResult), recorder);
            Check(proxy != null, "A runtime handler is created for the question contract.");
            var contract = proxy as P16SavePromptProbeContract;
            Check(contract != null, "The runtime handler implements the question contract exactly.");

            Check(contract.ShowQuestion(P16SaveQuestionPolicy.VerifiedSchemaPromptText, "SewerGEMS", false) == P16SavePromptProbeResult.Yes,
                "The authorised schema prompt is answered Yes.");
            Check(contract.ShowQuestion("An existing read-only hydraulic model file would be overwritten. Save anyway?", "SewerGEMS", false)
                    == P16SavePromptProbeResult.Cancel,
                "An unknown question is answered Cancel.");
            Check(contract.ShowQuestion(P16SavePromptModalTemplate(P16SavePromptForeignModel), "SewerGEMS", false) == P16SavePromptProbeResult.Cancel,
                "A schema prompt for a foreign model is answered Cancel.");

            Check(recorder.Count == 3, "Every question is recorded exactly once.");
            Check(recorder.Classifications.Length == 3 && recorder.Decisions.Length == 3, "Classification and decision are recorded per question.");
            Check(recorder.Classifications[0] == P16SaveQuestionPolicy.SchemaOverwriteAuthorized, "The first question is the authorised one.");
            Check(recorder.Decisions[0] == P16SaveQuestionPolicy.DecisionYes, "The first decision is Yes.");
            Check(recorder.Decisions[1] == P16SaveQuestionPolicy.DecisionCancel && recorder.Decisions[2] == P16SaveQuestionPolicy.DecisionCancel,
                "Both refused questions record Cancel.");
            Check(recorder.AnyUnauthorizedQuestion, "Seeing any unauthorised question marks the attempt unclean.");
            Check(recorder.Records[1].EmbeddedTargetPath == null, "A path-free unknown question records no target.");
            Check(recorder.Records[2].EmbeddedTargetPath == P16SavePromptForeignModel, "The refused modal target is recorded verbatim.");
            Check(recorder.Records.All(record => record.MessageSha256 != null && record.MessageSha256.Length == 64),
                "Every record carries a message digest rather than the message text.");

            // The proxy must also survive the Object members DispatchProxy forwards.
            Check(!string.IsNullOrEmpty(proxy.ToString()), "ToString is answered without throwing.");
            Check(recorder.Count == 3, "Object members are not recorded as questions.");
        }

        // The emitted type must satisfy the inherited IMessageHandler member as well as the question member.
        private static void P16SavePrompt_HandlerImplementsInheritedWarningContract()
        {
            var recorder = new P16SaveQuestionRecorder(P16SavePromptAuthorizedModel);
            object proxy = P16SaveQuestionHandler.Create(typeof(P16SavePromptProbeContract), typeof(P16SavePromptProbeResult), recorder);
            Check(proxy is P16SavePromptProbeContract, "The emitted type implements the question interface.");
            Check(proxy is P16SavePromptProbeMessageHandler, "The emitted type also implements the inherited message-handler interface.");

            var messageHandler = (P16SavePromptProbeMessageHandler)proxy;
            messageHandler.ShowWarning("A vendor warning must be recorded without opening UI.");
            Check(recorder.WarningCount == 1, "ShowWarning is recorded once.");
            Check(recorder.WarningDigests.Length == 1 && recorder.WarningDigests[0].Length == 64,
                "ShowWarning records only a SHA-256 digest.");
            Check(!recorder.WarningDigests[0].Contains("vendor warning"),
                "The warning text itself is not stored.");
            Check(recorder.Count == 0, "ShowWarning is not misclassified as a question.");

            var questions = (P16SavePromptProbeContract)proxy;
            Check(questions.ShowQuestion(P16SaveQuestionPolicy.VerifiedSchemaPromptText, "SewerGEMS", false) == P16SavePromptProbeResult.Yes,
                "The inherited warning implementation does not break question dispatch.");
            Check(recorder.Count == 1 && recorder.Decisions[0] == P16SaveQuestionPolicy.DecisionYes,
                "Question policy continues to record the authorized decision.");
        }

        // A handler with no bound working model may never answer anything affirmatively.
        private static void P16SavePrompt_UnboundHandlerRefusesEverything()
        {
            var recorder = new P16SaveQuestionRecorder(null);
            object proxy = P16SaveQuestionHandler.Create(typeof(P16SavePromptProbeContract), typeof(P16SavePromptProbeResult), recorder);
            var contract = (P16SavePromptProbeContract)proxy;

            Check(contract.ShowQuestion(P16SaveQuestionPolicy.VerifiedSchemaPromptText, "SewerGEMS", false) == P16SavePromptProbeResult.Cancel,
                "An unbound handler refuses the path-free schema prompt.");
            Check(contract.ShowQuestion(P16SavePromptModalTemplate(P16SavePromptAuthorizedModel), "SewerGEMS", false) == P16SavePromptProbeResult.Cancel,
                "An unbound handler refuses the modal schema prompt even for the right-looking path.");
            Check(recorder.Classifications.All(c => c == P16SaveQuestionPolicy.SchemaOverwriteTargetNotBound),
                "Both refusals are recorded as an unbound target.");
            Check(recorder.AnyUnauthorizedQuestion, "An unbound refusal is never a clean attempt.");
        }

        // The question log is bounded and everything past the budget is refused.
        private static void P16SavePrompt_QuestionBudgetIsBounded()
        {
            var recorder = new P16SaveQuestionRecorder(P16SavePromptAuthorizedModel);
            object proxy = P16SaveQuestionHandler.Create(typeof(P16SavePromptProbeContract), typeof(P16SavePromptProbeResult), recorder);
            var contract = (P16SavePromptProbeContract)proxy;

            for (int i = 0; i < P16SaveQuestionPolicy.MaxQuestions + 5; i++)
                Check(contract.ShowQuestion("Unexpected question " + i, "SewerGEMS", false) == P16SavePromptProbeResult.Cancel,
                    "Every over-budget question is answered Cancel.");

            Check(recorder.Count == P16SaveQuestionPolicy.MaxQuestions + 1,
                "The recorder stops growing one record past its budget.");
            Check(recorder.BudgetExceeded, "Exhausting the budget is recorded explicitly.");
            Check(recorder.Classifications.Last() == P16SaveQuestionPolicy.QuestionBudgetExceeded,
                "The terminal record is the budget marker.");
            Check(recorder.AnyUnauthorizedQuestion, "An exhausted budget is never a clean attempt.");
        }

        // IProject.Save(ProjectProperties) returns void, so a success claim can only come from the
        // inventory delta, never from a return value.
        private static void P16SaveResult_VoidReturnIsNeverTreatedAsSuccess()
        {
            Check(P16ProjectPersistenceResult.Classify(false, false, 0, null) == P16ProjectPersistenceResult.NotPersisted,
                "No exception and no written file is not persistence.");
            Check(P16ProjectPersistenceResult.Classify(false, false, 0, false) == P16ProjectPersistenceResult.NotPersisted,
                "A clean project with no written file is not persistence.");
            Check(P16ProjectPersistenceResult.Classify(false, false, 2, false) == P16ProjectPersistenceResult.Persisted,
                "A written file with a clean project is persistence.");
            Check(P16ProjectPersistenceResult.Classify(false, false, 2, null) == P16ProjectPersistenceResult.Persisted,
                "An unreadable dirty flag does not by itself deny a written file.");
            Check(P16ProjectPersistenceResult.Classify(false, false, 2, true) == P16ProjectPersistenceResult.NotPersisted,
                "A vendor save that still reports the project dirty is not accepted.");
            Check(P16ProjectPersistenceResult.Classify(true, false, 0, null) == P16ProjectPersistenceResult.Failed,
                "An exception is a failed persistence attempt.");
            Check(P16ProjectPersistenceResult.Classify(true, false, 3, false) == P16ProjectPersistenceResult.Failed,
                "An exception outranks any inventory delta.");
            Check(P16ProjectPersistenceResult.Classify(false, true, 3, false) == P16ProjectPersistenceResult.UncleanQuestionSeen,
                "An unauthorised question outranks any inventory delta.");
            Check(P16ProjectPersistenceResult.Classify(true, true, 3, false) == P16ProjectPersistenceResult.Failed,
                "An exception outranks an unclean question.");
            Check(P16ProjectPersistenceResult.IsPersisted(P16ProjectPersistenceResult.Persisted), "PERSISTED is the only success classification.");
            foreach (string other in new[] { P16ProjectPersistenceResult.NotPersisted, P16ProjectPersistenceResult.Failed, P16ProjectPersistenceResult.UncleanQuestionSeen })
                Check(!P16ProjectPersistenceResult.IsPersisted(other), "Only PERSISTED counts as success: " + other);
        }

        // The persistence boundary must name the verified non-UI member, and the UI-bound call must be
        // gone from the host source.
        private static void P16SaveResult_BoundaryIsTheVerifiedNonUiMember()
        {
            Check(P16AzadeganCalculationHost.PersistenceBoundary == "NON_UI_PROJECT_SAVE",
                "The recorded boundary is the non-UI project save.");
            Check(P16AzadeganCalculationHost.PersistenceMemberSignature ==
                    "Haestad.Framework.Application.IProject.Save(Haestad.Framework.Application.ProjectProperties)",
                "The recorded member is the verified IProject.Save(ProjectProperties) signature.");
            Check(P16AzadeganCalculationHost.PersistenceStageName == "ProjectPersistence",
                "The journal stage name is the persistence stage.");
            Check(SaveStageWatchdog.SaveStageName == P16AzadeganCalculationHost.PersistenceStageName,
                "The save watchdog still bounds the persistence stage.");

            string root = Path.Combine(ResolveRepositoryRoot(), "src", "SewerPilot.OpenFlows.Adapter");
            string host = File.ReadAllText(Path.Combine(root, "P16AzadeganCalculationHost.cs"));
            Check(!host.Contains("NameSaveCurrentProject"), "The host no longer resolves the UI-bound member.");
            Check(!host.Contains("\"SaveCurrentProject\""), "The host carries no literal UI-bound member token.");
            Check(!host.Contains(".SaveCurrentProject("), "The host no longer calls the UI-bound member.");
            Check(host.Contains("PersistWorkingCopy("), "The host persists through the non-UI boundary.");
            Check(host.Contains("AdapterWriteBinding.ExecuteSave("), "Persistence still goes through the audited write gateway.");

            // The new handler sources stay inside the read-path architectural guard.
            foreach (string file in new[] { "P16SaveQuestionHandler.cs" })
            {
                string source = File.ReadAllText(Path.Combine(root, file));
                foreach (string forbidden in new[] { "\"Save\"", ".Save(", ".SaveAs(", "\"SaveAs\"", ".SetValue(", "\"SaveCurrentProject\"", ".SaveCurrentProject(" })
                    Check(!source.Contains(forbidden), file + " must not reference " + forbidden + ".");
                Check(source.Contains("TypeBuilder"), "The handler satisfies the vendor contract through a type emitted at run time, not a compile-time reference.");
            }

            // No compile-time Bentley dependency may have been introduced by this mission.
            string adapterProject = File.ReadAllText(Path.Combine(root, "SewerPilot.OpenFlows.Adapter.csproj"));
            Check(!adapterProject.Contains("Haestad") && !adapterProject.Contains("Bentley"),
                "The adapter project still carries no vendor reference.");
        }
    }
}
