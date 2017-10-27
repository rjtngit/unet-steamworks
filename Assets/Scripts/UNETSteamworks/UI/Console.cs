using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Console : MonoBehaviour {

    const int MAX_LINES = 200; 

    public Text text;
    public ScrollRect scrollRect;

    void OnEnable () {
        Application.logMessageReceived += HandleLog;
    }

    void OnDisable () {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        Color color;

        switch (type)
        {
            case LogType.Error:
            case LogType.Exception:
                color = Color.red;
                break;
            case LogType.Warning:
                color = Color.yellow;
                break;
            default:
                color = Color.white;
                break;
        }

        // Append log output
        text.text += "\n<color=#" + ColorUtility.ToHtmlStringRGB(color) +">" + logString + "</color>";

        // Truncate text if it's too long
        Canvas.ForceUpdateCanvases();
        if (text.cachedTextGenerator.lineCount > MAX_LINES)
        {
            var firstLine = text.cachedTextGenerator.lines[text.cachedTextGenerator.lineCount - MAX_LINES ];
            text.text = text.text.Substring(firstLine.startCharIdx);
        }

        // Scroll to bottom
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }
}
