// 소리지르기 - docs/minigames/07_소리지르기.md 스펙.
//
// 규칙: 포즈/표정은 전혀 안 본다 - PoseInputHub의 신규 필드인 VoiceLevel(마이크 음량,
// 0~1로 정규화)만 쓴다. 상태는 플레이어별이 아니라 매치 전체에 하나: "지금 누구 턴인지"와
// "이번에 넘어야 할 기준 음량(requiredLevel)". 턴 소유자가 turnSeconds 동안 지르는 소리의
// 최댓값(peakLevel)이 requiredLevel보다 "엄격히" 커야 성공 - 같거나 작으면 그 자리에서
// 즉시 패배하고 상대가 승리한다(무승부 없음, 턴이 명확히 교대되므로 동시 패배 자체가 없음).
// 성공할 때마다 requiredLevel이 이번 peakLevel로 갱신되고 턴이 상대로 넘어가므로, 요구
// 음량은 계속 올라가기만 한다 - 결국 누군가 못 넘기는 순간 게임이 끝난다.
//
// 첫 턴(P1)은 아직 기준이 없으므로 baselineRequiredLevel(아주 낮은 값)만 넘으면 된다.
//
// 캐릭터 표현: 관절 리깅 없음 - p1Face/p2Face는 옆모습 표정 그림 한 장을 그대로 보여주는
// SpriteRenderer다. idle_side/shout_side/shout_swollen 세 장 중 하나로 스프라이트만
// 바꿔치기하고(프레임 시퀀스가 아니라 개별 파츠라 FrameAnimatedCharacter 대신 직접 스왑),
// 매번 ArtAssets.FitWidth로 폭을 다시 맞춘다. 음량이 커질수록 크기와 붉은 색을 연속적으로
// 올리고, 소리가 끝나면 같은 강도를 낮춰 swollen → shout → idle 순서로 되돌린다.
using System.Collections;
using UnityEngine;

public class ScreamDuelGame : MonoBehaviour
{
    public float turnSeconds = 3f;
    public float baselineRequiredLevel = 0.05f;
    public float maxFaceScale = 2.2f;
    public float resultDisplaySeconds = 2f;
    // 턴 소유자의 실시간 음량이 이 값을 넘으면(peak가 아니라 그 순간 값) 표정을 한 단계 더
    // 부풀어 오른 shout_swollen으로 바꾼다 - 순전히 연출용, 판정에는 영향 없음.
    public float shoutLevelThreshold = 0.08f;
    [Range(0f, 1f)] public float swollenExpressionThreshold = 0.78f;
    public float expressionRiseSpeed = 2.5f;
    public float expressionFallSpeed = 1.5f;
    public Color maxScreamTint = new Color(1f, 0.42f, 0.36f, 1f);
    public float p1FaceWidth = 3f; // 얼굴 기본 표시 폭(월드 유닛) - 음량에 따라 maxFaceScale까지 확대
    public float p2FaceWidth = 3f;
    public float p1FaceX = -2.9f;
    public float p2FaceX = 2.9f;
    public float maxOutwardShift = 0.75f;
    public float faceBottomOffset = -0.05f;

    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private SpriteRenderer p1Face;
    [SerializeField] private SpriteRenderer p2Face;
    [SerializeField] private ScreamDuelHud hud;

    private Sprite _p1Idle, _p1Shout, _p1Swollen;
    private Sprite _p2Idle, _p2Shout, _p2Swollen;
    private float _p1Expression;
    private float _p2Expression;

    private PlayerId _turnOwner;
    private float _requiredLevel;
    private float _peakLevel;
    private float _turnElapsed;
    private bool _ended;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        _p1Idle = ArtAssets.LoadCharacter(PlayerId.P1, "scream_idle");
        _p1Shout = ArtAssets.LoadCharacter(PlayerId.P1, "scream_shout");
        _p1Swollen = ArtAssets.LoadCharacter(PlayerId.P1, "scream_shout_swollen");
        _p2Idle = ArtAssets.LoadCharacter(PlayerId.P2, "scream_idle");
        _p2Shout = ArtAssets.LoadCharacter(PlayerId.P2, "scream_shout");
        _p2Swollen = ArtAssets.LoadCharacter(PlayerId.P2, "scream_shout_swollen");

        EnsureSceneObjects();
        if (p1Face == null || p2Face == null || hud == null)
        {
            Debug.LogError($"{nameof(ScreamDuelGame)}: 화면 오브젝트를 생성하지 못했습니다. " +
                           "ScreamDuel 이미지가 Resources에 임포트됐는지 확인하세요.", this);
            enabled = false;
            return;
        }

        _requiredLevel = baselineRequiredLevel;
        ApplyFace(p1Face, _p1Idle, p1FaceWidth, 0f, p1FaceX);
        ApplyFace(p2Face, _p2Idle, p2FaceWidth, 0f, p2FaceX);
        BeginTurn(PlayerId.P1);
    }

    // 예전 ScreamDuel.unity는 카메라/조명/GameController만 있는 빈 뼈대 씬이다.
    // 에디터용 Build Scene 메뉴를 아직 실행하지 않았더라도 게임 화면이 나오도록 런타임에
    // 빠진 배경, 캐릭터, HUD만 보충한다. 이미 씬에 연결돼 있으면 기존 수동 배치를 보존한다.
    private void EnsureSceneObjects()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
        }
        if (GameObject.Find("Background") == null && cam != null)
            ArtAssets.CreateBackground(cam, ArtAssets.LoadScreamDuel("background"));

        if (p1Face == null)
            p1Face = FindOrCreateFace("P1 Face", _p1Idle, new Vector3(p1FaceX, -0.5f, 0f));
        if (p2Face == null)
            p2Face = FindOrCreateFace("P2 Face", _p2Idle, new Vector3(p2FaceX, -0.5f, 0f));

        if (hud == null)
        {
            hud = FindAnyObjectByType<ScreamDuelHud>();
            if (hud == null) hud = ScreamDuelHud.Build(turnSeconds);
        }
    }

    private static SpriteRenderer FindOrCreateFace(string objectName, Sprite sprite, Vector3 position)
    {
        GameObject existing = GameObject.Find(objectName);
        SpriteRenderer renderer = existing != null ? existing.GetComponent<SpriteRenderer>() : null;
        if (renderer == null)
        {
            var go = new GameObject(objectName);
            go.transform.position = position;
            renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 1;
        }

        renderer.sprite = sprite;
        return renderer;
    }

    private void BeginTurn(PlayerId owner)
    {
        _turnOwner = owner;
        _peakLevel = 0f;
        _turnElapsed = 0f;
        hud?.SetTurn(owner);
        hud?.ShowEvent($"{owner} 턴 - 상대보다 크게 질러라!");
    }

    private void Update()
    {
        if (_ended)
        {
            UpdateFaceExpressions(0f, false, false);
            return;
        }

        PlayerPoseState state = PoseInputHub.Instance?.Get(_turnOwner);
        // 마이크가 안 잡히면(IsVoiceTracked == false) 그 프레임 음량은 0으로 취급한다.
        float level = (state != null && state.IsVoiceTracked) ? state.VoiceLevel : 0f;

        // 턴 동안의 최댓값만 채점에 쓴다 - 과일따기의 PeakHeight와 같은 방식(중간에 작아져도
        // peakLevel은 줄지 않는다).
        if (level > _peakLevel) _peakLevel = level;
        _turnElapsed += Time.deltaTime;

        bool isP1Turn = _turnOwner == PlayerId.P1;
        UpdateFaceExpressions(level, isP1Turn, !isP1Turn);

        hud?.SetLevels(_requiredLevel, level, _peakLevel);
        hud?.SetTurnTimeRemaining(turnSeconds - _turnElapsed);

        if (_turnElapsed >= turnSeconds)
            ResolveTurn();
    }

    private void UpdateFaceExpressions(float level, bool p1Speaking, bool p2Speaking)
    {
        float target = Mathf.InverseLerp(shoutLevelThreshold, 1f, Mathf.Clamp01(level));
        _p1Expression = MoveExpression(_p1Expression, p1Speaking ? target : 0f);
        _p2Expression = MoveExpression(_p2Expression, p2Speaking ? target : 0f);

        float p1X = p1FaceX - maxOutwardShift * _p1Expression;
        float p2X = p2FaceX + maxOutwardShift * _p2Expression;
        ApplyFace(p1Face, FaceFor(_p1Expression, _p1Idle, _p1Shout, _p1Swollen),
            p1FaceWidth, _p1Expression, p1X);
        ApplyFace(p2Face, FaceFor(_p2Expression, _p2Idle, _p2Shout, _p2Swollen),
            p2FaceWidth, _p2Expression, p2X);
    }

    private float MoveExpression(float current, float target)
    {
        float speed = target > current ? expressionRiseSpeed : expressionFallSpeed;
        return Mathf.MoveTowards(current, target, speed * Time.deltaTime);
    }

    private Sprite FaceFor(float expression, Sprite idle, Sprite shout, Sprite swollen)
    {
        if (expression <= 0.01f) return idle;
        return expression >= swollenExpressionThreshold ? swollen : shout;
    }

    private void ApplyFace(SpriteRenderer renderer, Sprite sprite, float baseWidth, float expression, float x)
    {
        if (renderer == null || sprite == null) return;
        renderer.sprite = sprite;
        renderer.color = Color.Lerp(Color.white, maxScreamTint, expression);
        ArtAssets.FitWidth(renderer, baseWidth * Mathf.Lerp(1f, maxFaceScale, expression));

        Vector3 position = renderer.transform.position;
        position.x = x;
        renderer.transform.position = position;

        // 스프라이트의 피벗/원본 여백이 서로 달라도 실제 렌더링 Bounds의 하단을 카메라 하단에
        // 다시 맞춘다. 확대된 몸통은 화면 밖으로 내려가고 얼굴/상체가 위로 부푸는 구도가 된다.
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            float screenBottom = cam.transform.position.y - cam.orthographicSize + faceBottomOffset;
            position = renderer.transform.position;
            position.y += screenBottom - renderer.bounds.min.y;
            renderer.transform.position = position;
        }
    }

    private void ResolveTurn()
    {
        // 엄격히 더 커야만 성공 - 같거나 작으면 그 자리에서 즉시 패배(재시도 없음).
        if (_peakLevel <= _requiredLevel)
        {
            PlayerId winner = _turnOwner == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1;
            EndMatch(winner);
            return;
        }

        _requiredLevel = _peakLevel;
        BeginTurn(_turnOwner == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1);
        hud?.ShowEvent("성공!");
    }

    private void EndMatch(PlayerId winner)
    {
        _ended = true;
        hud?.ShowEvent($"{winner} 승리!", resultDisplaySeconds);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }
}
