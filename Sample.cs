#:project src/TypeSafe/TypeSafe.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true

using TypeSafe;

using var client = new TypeSafeClient(new TypeSafeClientOptions
{
    ApiKey = "your-api-key"
});

var result = await client.SystemOneAsync(new SystemOneRequest
{
    State = new { text = "Evaluate this statement." },
    Questions = new Dictionary<string, Question>
    {
        ["safe"] = Questions.Noul(
            instructions: "Assess safety.",
            criteria: new NoulCriteria
            {
                True = "The statement is safe.",
                False = "The statement is unsafe."
            }),
        ["topic"] = Questions.Choice(
            instructions: new { task = "Choose a topic." },
            criteria: new Dictionary<string, object?>
            {
                ["science"] = "Scientific content",
                ["other"] = null
            }),
        ["quality"] = Questions.Score(
            instructions: "Rate quality.",
            criteria: ["poor", new { label = "good" }, "excellent"])
    }
});

var safeProbability = ((NoulAnswer)result.Answers["safe"]).Noul;
Console.WriteLine($"Safe probability: {safeProbability}");