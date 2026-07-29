// 네트워크 계층 공용 타입. 게임 이벤트는 전부 이 NetworkEvent 봉투에 담아 JSON 한 줄로
// 주고받는다 - JsonUtility가 Dictionary/다형성을 못 다루므로, payload는 각 이벤트 타입별
// 전용 [Serializable] 클래스를 JsonUtility로 직렬화한 문자열을 다시 한 번 담는 방식이다.
using System;

public enum NetworkRole { Offline, Host, Client }

public enum NetworkConnectionState { Disconnected, Listening, Connecting, Connected }

[Serializable]
public class NetworkEvent
{
    public string type;
    // 이 이벤트를 보낸 쪽의 로컬 시계(Time.realtimeSinceStartupAsDouble) 기준 발생 시각.
    // 호스트가 보낸 이벤트라면 클라이언트가 NetworkSession.ClockOffsetToHost로 보정해서
    // "호스트 기준 지금"과 비교할 수 있다 (설계 문서 6장).
    public double senderTime;
    public string json;
}
