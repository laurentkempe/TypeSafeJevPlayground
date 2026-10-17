namespace TypeSafe.Cli;

public static class ResultMapper
{
    public static EvaluationResult Map(SystemOneResult result) =>
        new(
            result.Model,
            result.Answers.ToDictionary(pair => pair.Key, pair => MapAnswer(pair.Value)),
            new UsageResult(result.Usage.InputTokens, result.Usage.OutputTokens));

    private static AnswerResult MapAnswer(Answer answer) => answer switch
    {
        NoulAnswer noul => new AnswerResult("noul", Noul: noul.Noul),
        ChoiceAnswer choice => new AnswerResult(
            "choice",
            Choice: choice.Choice,
            Confidence: choice.Confidence,
            Probabilities: choice.Probabilities),
        ScoreAnswer score => new AnswerResult(
            "score",
            Score: score.Score,
            Confidence: score.Confidence,
            Probabilities: score.Probabilities,
            Legend: score.Legend),
        BoxAnswer box => new AnswerResult("box", Confidence: box.Confidence, Box: box.Box),
        _ => new AnswerResult(answer.Type)
    };
}
