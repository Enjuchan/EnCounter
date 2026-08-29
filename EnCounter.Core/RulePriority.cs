namespace EnCounter.Core;

/// <summary>
/// Welche von mehreren passenden Regeln die Meldung stellt.
///
/// **Gezählt wird immer von allen** - das ist unabhängig hiervon und ändert sich nie.
/// Angezeigt werden kann aber nur eine, und bisher war das schlicht die erste in der
/// Liste. Wer eine allgemeine „alle Streicheleien"-Regel hatte und daneben eine nur
/// für eine bestimmte Person, bekam immer den allgemeinen Text zu sehen: Die
/// persönliche Regel zählte zwar mit, kam aber nie zu Wort.
///
/// Die naheliegende Abhilfe wäre eine Ausschlussliste in der allgemeinen Regel gewesen.
/// Sie hätte dieselbe Angabe an zwei Stellen verlangt, und wer eine davon vergisst, hat
/// wieder das alte Verhalten - ohne zu wissen, warum.
///
/// Stattdessen entscheidet die **Genauigkeit**: Wer enger eingrenzt, meint es
/// bestimmter und gewinnt. Das braucht keine Einstellung und ist in einem Satz erklärt.
/// </summary>
public static class RulePriority
{
    /// <summary>
    /// Je höher, desto spezifischer.
    ///
    /// Eine Namensliste wiegt schwerer als eine Auswahl von Emotes oder Wörtern: Sie
    /// benennt eine einzelne Person, und genau dann will man einen eigenen Text.
    /// </summary>
    public static int Of(Rule rule)
    {
        var punkte = 0;

        if (rule.OnlyFrom.Count > 0)
            punkte += 2;

        if (rule.EmoteIds.Count > 0 || rule.TriggerTexts.Count > 0)
            punkte += 1;

        return punkte;
    }

    /// <summary>
    /// Die Regel, die die Meldung stellt. Bei gleicher Genauigkeit gewinnt die, die
    /// weiter oben steht - dort ist die Reihenfolge wieder die Antwort.
    /// </summary>
    public static Rule? Winner(IEnumerable<Rule> passende)
    {
        Rule? beste = null;
        var bestePunkte = int.MinValue;

        foreach (var regel in passende)
        {
            var punkte = Of(regel);

            if (punkte <= bestePunkte)
                continue;

            beste = regel;
            bestePunkte = punkte;
        }

        return beste;
    }
}
