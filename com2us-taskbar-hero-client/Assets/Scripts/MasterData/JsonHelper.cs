using UnityEngine;

namespace TaskbarHero.Client.MasterData
{
    /// <summary>
    /// Unity <see cref="JsonUtility"/> 는 최상위 배열("[ ... ]")을 직접 파싱하지 못한다.
    /// 번들 마스터 데이터 JSON 은 "테이블별 배열"이므로, 객체로 감싸 파싱하는 헬퍼를 둔다
    /// (마스터 데이터 기획서 §7.2).
    /// </summary>
    public static class JsonHelper
    {
        [System.Serializable]
        private class Wrapper<T>
        {
            public T[] items;
        }

        /// <summary>최상위 JSON 배열 문자열을 T 배열로 파싱한다. 빈/널 입력은 빈 배열.</summary>
        public static T[] FromJsonArray<T>(string arrayJson)
        {
            if (string.IsNullOrWhiteSpace(arrayJson))
            {
                return new T[0];
            }

            string wrapped = "{\"items\":" + arrayJson + "}";
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(wrapped);
            return wrapper != null && wrapper.items != null ? wrapper.items : new T[0];
        }
    }
}
