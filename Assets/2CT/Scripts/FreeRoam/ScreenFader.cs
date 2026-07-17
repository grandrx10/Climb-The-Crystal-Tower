using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// Full-screen black fade used by portals/scene transitions. Auto-created singleton that
    /// survives scene loads and fades back in whenever a new scene finishes loading.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        private static ScreenFader _instance;
        private CanvasGroup _cg;
        private Coroutine _routine;

        public static ScreenFader Instance
        {
            get { if (_instance == null) Create(); return _instance; }
        }

        private static void Create()
        {
            var go = new GameObject("ScreenFader");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ScreenFader>();

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            var imgGO = new GameObject("Black");
            imgGO.transform.SetParent(go.transform, false);
            var img = imgGO.AddComponent<Image>();
            img.color = Color.black;
            var rt = (RectTransform)imgGO.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

            _instance._cg = go.AddComponent<CanvasGroup>();
            _instance._cg.alpha = 0f;
            _instance._cg.blocksRaycasts = false;
            SceneManager.sceneLoaded += _instance.OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => FadeIn(0.4f);

        public static void FadeOut(float duration) => Instance.StartFade(1f, duration);
        public static void FadeIn(float duration) => Instance.StartFade(0f, duration);

        private void StartFade(float targetAlpha, float duration)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(FadeRoutine(targetAlpha, duration));
        }

        private IEnumerator FadeRoutine(float target, float duration)
        {
            _cg.blocksRaycasts = target > 0.5f;
            float start = _cg.alpha, t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _cg.alpha = Mathf.Lerp(start, target, duration > 0 ? t / duration : 1f);
                yield return null;
            }
            _cg.alpha = target;
            _cg.blocksRaycasts = target > 0.5f;
        }
    }
}
