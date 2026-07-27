using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GlassShooter.Gameplay
{
    /// <summary>
    /// クラック・弱点グラフの不整合を、ConsoleとJSON Linesファイルへ記録します。
    /// ゲーム挙動は変更せず、CrackProcessingComponentから渡された診断イベントだけを扱います。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrackDiagnosticsLogger : MonoBehaviour
    {
        [Header("Outputs")]
        [SerializeField] private bool diagnosticsEnabled = true;
        [SerializeField] private bool logRoutineEvents;
        [SerializeField] private bool writeToConsole = true;
        [SerializeField] private bool writeJsonLinesFile = true;

        [Header("Runtime State (Read Only)")]
        [SerializeField] private int impactSequence;
        [SerializeField] private int consecutiveNoProgressImpacts;
        [SerializeField, TextArea(2, 6)] private string lastDiagnostic;
        [SerializeField] private string currentLogFilePath;

        private static string sessionLogFilePath;
        private bool hasObservedWeakPoint;
        private bool lastWeakPointOnBoundary;
        private bool lastWeakPointAtVertex;
        private string boundOwnerEntityId;
        private readonly Dictionary<string, float> internalVulnerabilityByPosition =
            new Dictionary<string, float>();

        public bool DiagnosticsEnabled => diagnosticsEnabled &&
            EnvironmentManager.Instance != null &&
            EnvironmentManager.Instance.CrackDiagnosticsEnabled;
        public string CurrentLogFilePath => currentLogFilePath;
        public int ImpactSequence => impactSequence;

        internal void Bind(CrackProcessingComponent owner)
        {
            if (owner == null || !DiagnosticsEnabled)
            {
                return;
            }
            string ownerEntityId = owner.GetEntityId().ToString();
            if (boundOwnerEntityId == ownerEntityId)
            {
                return;
            }

            boundOwnerEntityId = ownerEntityId;
            currentLogFilePath = ResolveSessionLogFilePath();
            Record(
                "LOGGER_READY",
                false,
                $"seed={owner.CrackRandomSeedForDiagnostics} logFile={currentLogFilePath}",
                true);
        }

        internal void BeginImpact(
            Vector2 localImpact,
            Vector2 velocity,
            float newEnergy,
            float pooledBefore,
            float scanRadius)
        {
            impactSequence++;
            Record(
                "IMPACT_BEGIN",
                false,
                $"impact={Format(localImpact)} velocity={Format(velocity)} " +
                $"newEnergy={Format(newEnergy)} pooledBefore={Format(pooledBefore)} " +
                $"scanRadius={Format(scanRadius)}",
                true);
        }

        internal void ObserveWeakPoint(
            string stage,
            Vector2 position,
            bool nodeExists,
            bool onBoundary,
            bool atVertex,
            int degree,
            int nodeCount,
            int connectionCount)
        {
            bool becameBoundary = hasObservedWeakPoint &&
                !lastWeakPointOnBoundary &&
                onBoundary;
            bool becameVertex = hasObservedWeakPoint &&
                !lastWeakPointAtVertex &&
                atVertex;
            bool suspicious = onBoundary && degree < 2;

            Record(
                becameVertex
                    ? "WEAKPOINT_BECAME_OUTLINE_VERTEX"
                    : becameBoundary
                        ? "WEAKPOINT_BECAME_BOUNDARY"
                        : suspicious
                            ? "WEAKPOINT_ON_BOUNDARY_WITH_LOW_DEGREE"
                            : "WEAKPOINT_STATE",
                becameBoundary || becameVertex || suspicious,
                $"stage={stage} position={Format(position)} nodeExists={nodeExists} " +
                $"onBoundary={onBoundary} atVertex={atVertex} degree={degree} " +
                $"nodes={nodeCount} connections={connectionCount}",
                suspicious || becameBoundary || becameVertex);

            hasObservedWeakPoint = true;
            lastWeakPointOnBoundary = onBoundary;
            lastWeakPointAtVertex = atVertex;
        }

        internal void RecordWeakPointEvaluation(int degree, bool defeated)
        {
            Record(
                defeated ? "WEAKPOINT_DEFEATED" : "WEAKPOINT_DEGREE_EVALUATED",
                !defeated && degree < 2,
                $"degree={degree} threshold=2 defeated={defeated}",
                defeated || degree > 0);
        }

        internal void RecordVulnerabilityInvariant(
            string code,
            string stage,
            Vector2 position,
            float expected,
            float actual)
        {
            bool mismatch = !Mathf.Approximately(expected, actual);
            Record(
                mismatch ? code : "VULNERABILITY_INVARIANT_OK",
                mismatch,
                $"stage={stage} position={Format(position)} " +
                $"expected={Format(expected)} actual={Format(actual)}",
                mismatch || logRoutineEvents);
        }

        internal void RecordVulnerabilityRangeInvariant(
            string code,
            string stage,
            Vector2 position,
            float minimum,
            float maximum,
            float actual)
        {
            bool mismatch = actual < minimum || actual > maximum;
            Record(
                mismatch ? code : "VULNERABILITY_RANGE_INVARIANT_OK",
                mismatch,
                $"stage={stage} position={Format(position)} " +
                $"minimum={Format(minimum)} maximum={Format(maximum)} actual={Format(actual)}",
                mismatch || logRoutineEvents);
        }

        internal void TrackInternalVulnerability(
            string stage,
            Vector2 position,
            float actual,
            bool isWeakPoint)
        {
            string key = Format(position);
            if (!internalVulnerabilityByPosition.TryGetValue(key, out float previous))
            {
                internalVulnerabilityByPosition[key] = actual;
                return;
            }

            bool changed = !Mathf.Approximately(previous, actual);
            Record(
                isWeakPoint
                    ? "WEAKPOINT_VULNERABILITY_CHANGED"
                    : "INTERNAL_POINT_VULNERABILITY_CHANGED",
                changed,
                $"stage={stage} position={key} previous={Format(previous)} " +
                $"actual={Format(actual)} isWeakPoint={isWeakPoint}",
                changed);
            internalVulnerabilityByPosition[key] = actual;
        }

        internal void RecordWeakPointStartNode(Vector2 position, int degree)
        {
            Record(
                "WEAKPOINT_SELECTED_AS_GROWTH_START",
                true,
                $"position={Format(position)} degree={degree}",
                true);
        }

        internal void RecordImpactGraphRelation(
            Vector2 impact,
            Vector2 surfaceRoot,
            Vector2 growthStart,
            bool surfaceRootCreated,
            bool startIsSurfaceRoot,
            bool startIsWeakPoint,
            int startDegree)
        {
            Record(
                "IMPACT_GRAPH_RELATION",
                startIsWeakPoint,
                $"impact={Format(impact)} surfaceRoot={Format(surfaceRoot)} " +
                $"growthStart={Format(growthStart)} surfaceRootCreated={surfaceRootCreated} " +
                $"impactToSurface={Format(Vector2.Distance(impact, surfaceRoot))} " +
                $"surfaceToStart={Format(Vector2.Distance(surfaceRoot, growthStart))} " +
                $"startIsSurfaceRoot={startIsSurfaceRoot} startIsWeakPoint={startIsWeakPoint} " +
                $"startDegree={startDegree}",
                true);
        }

        internal void RecordWeakPointBypass(
            string stage,
            Vector2 from,
            Vector2 to,
            Vector2 weakPoint,
            float distance,
            int weakPointDegree)
        {
            Record(
                "CONNECTION_BYPASSES_WEAKPOINT",
                true,
                $"stage={stage} from={Format(from)} to={Format(to)} " +
                $"weakPoint={Format(weakPoint)} distance={Format(distance)} " +
                $"weakPointDegree={weakPointDegree}",
                true);
        }

        internal void RecordSurfaceParallelRejection(
            Vector2 from,
            Vector2 target,
            bool targetsWeakPoint,
            float threshold)
        {
            Record(
                targetsWeakPoint
                    ? "WEAKPOINT_REJECTED_AS_SURFACE_PARALLEL"
                    : "SURFACE_PARALLEL_CANDIDATE_REJECTED",
                targetsWeakPoint,
                $"from={Format(from)} target={Format(target)} " +
                $"targetsWeakPoint={targetsWeakPoint} threshold={Format(threshold)}",
                true);
        }

        internal void RecordBoundaryFallback(
            bool success,
            string reason,
            Vector2 origin,
            Vector2 direction,
            float maximumDistance,
            float availableEnergy,
            Vector2 boundaryPoint)
        {
            Record(
                success ? "BOUNDARY_FALLBACK_CANDIDATE" : "BOUNDARY_FALLBACK_UNAVAILABLE",
                !success,
                $"reason={reason} origin={Format(origin)} direction={Format(direction)} " +
                $"maximumDistance={Format(maximumDistance)} availableEnergy={Format(availableEnergy)} " +
                $"boundaryPoint={Format(boundaryPoint)}",
                true);
        }

        internal void RecordSplitWeakPoint(
            Vector2 weakPoint,
            bool liesOnCrack,
            bool explicitlyInPath,
            bool inFirstRegion,
            bool inSecondRegion,
            int degree)
        {
            bool suspicious = liesOnCrack && degree < 2;
            Record(
                suspicious
                    ? "SPLIT_LINE_CONTAINS_UNDEFEATED_WEAKPOINT"
                    : "SPLIT_WEAKPOINT_CHECK",
                suspicious,
                $"weakPoint={Format(weakPoint)} liesOnCrack={liesOnCrack} " +
                $"explicitlyInPath={explicitlyInPath} inFirst={inFirstRegion} " +
                $"inSecond={inSecondRegion} degree={degree}",
                true);
        }

        internal void EndImpact(
            bool progressed,
            int pathCandidateCount,
            float pooledAfter,
            bool separated,
            bool startWasWeakPoint)
        {
            if (progressed)
            {
                consecutiveNoProgressImpacts = 0;
            }
            else
            {
                consecutiveNoProgressImpacts++;
            }

            bool suspicious = !progressed && pooledAfter > 0f;
            Record(
                suspicious ? "IMPACT_ENERGY_POOLED_WITHOUT_PROGRESS" : "IMPACT_END",
                suspicious,
                $"progressed={progressed} pathCandidates={pathCandidateCount} " +
                $"pooledAfter={Format(pooledAfter)} separated={separated} " +
                $"startWasWeakPoint={startWasWeakPoint} " +
                $"consecutiveNoProgress={consecutiveNoProgressImpacts}",
                true);
        }

        internal void RecordEvent(string code, bool warning, string details, bool alwaysEmit = false)
        {
            Record(code, warning, details, alwaysEmit || warning || logRoutineEvents);
        }

        private void Record(string code, bool warning, string details, bool shouldEmit)
        {
            if (!DiagnosticsEnabled || !shouldEmit)
            {
                return;
            }

            string message =
                $"[CrackDiag][{code}][frame={Time.frameCount}][impact={impactSequence}]" +
                $"[{name}] {details}";
            lastDiagnostic = message;

            if (writeToConsole)
            {
                if (warning)
                {
                    Debug.LogWarning(message, this);
                }
                else
                {
                    Debug.Log(message, this);
                }
            }

            if (!writeJsonLinesFile)
            {
                return;
            }

            string path = ResolveSessionLogFilePath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var line = new StringBuilder(256);
            line.Append('{')
                .Append("\"utc\":\"").Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append("\",")
                .Append("\"frame\":").Append(Time.frameCount).Append(',')
                .Append("\"impact\":").Append(impactSequence).Append(',')
                .Append("\"object\":\"").Append(Escape(name)).Append("\",")
                .Append("\"entityId\":\"").Append(Escape(gameObject.GetEntityId().ToString())).Append("\",")
                .Append("\"code\":\"").Append(Escape(code)).Append("\",")
                .Append("\"warning\":").Append(warning ? "true" : "false").Append(',')
                .Append("\"details\":\"").Append(Escape(details)).Append("\"}")
                .AppendLine();

            try
            {
                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                writeJsonLinesFile = false;
                Debug.LogWarning(
                    $"[CrackDiag][FILE_WRITE_DISABLED] path={path} error={exception.Message}",
                    this);
            }
        }

        private string ResolveSessionLogFilePath()
        {
            if (!writeJsonLinesFile)
            {
                return string.Empty;
            }
            if (!string.IsNullOrEmpty(sessionLogFilePath))
            {
                currentLogFilePath = sessionLogFilePath;
                return sessionLogFilePath;
            }

            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "CrackDiagnostics");
                Directory.CreateDirectory(directory);
                sessionLogFilePath = Path.Combine(
                    directory,
                    $"crack-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
                currentLogFilePath = sessionLogFilePath;
                return sessionLogFilePath;
            }
            catch (Exception exception)
            {
                writeJsonLinesFile = false;
                Debug.LogWarning(
                    $"[CrackDiag][FILE_SETUP_FAILED] error={exception.Message}",
                    this);
                return string.Empty;
            }
        }

        private static string Format(float value) =>
            value.ToString("0.########", CultureInfo.InvariantCulture);

        private static string Format(Vector2 value) =>
            $"({Format(value.x)},{Format(value.y)})";

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
