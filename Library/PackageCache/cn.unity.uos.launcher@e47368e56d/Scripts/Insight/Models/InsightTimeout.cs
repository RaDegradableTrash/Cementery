using System;
using System.Collections;
using UnityEngine;

namespace Unity.UOS.Insight.Models
{
    public class InsightTimeout : MonoBehaviour
    {
        // Use this for initialization
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }

        public static void SetTimeout(int timeout, Action<object> action, object obj)
        {
            GameObject gameObject = new GameObject("InsightTimeout");
            var tdTimeout = gameObject.AddComponent<InsightTimeout>();
            tdTimeout._setTimeout(timeout, action, obj);
        }

        private void _setTimeout(int timeout, Action<object> action, object obj)
        {
            StartCoroutine(_wait(timeout, action, obj));
        }

        private IEnumerator _wait(int timeout, Action<object> action, object obj)
        {
            yield return new WaitForSeconds(timeout);
            if (action != null)
            {
                action(obj);
            }
            Destroy(gameObject);
        }
    }
}