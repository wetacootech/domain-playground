namespace WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Common;

public record QuestionAnswer(string Question, string QuestionType, string? Answer, string? Visibility) : ValueObject;

public class Question : Entity
{
    public Question() { Id = NextEntityId("q"); }
    public string QuestionTemplateId { get; set; } = "";
    public QuestionAnswer Data { get; set; } = new("", "", null, null);
    public bool IsCompleted => Data.Answer != null;
}

public class Questionnaire : AggregateRoot
{
    public Questionnaire() { Id = NextId("quest"); }
    public List<Question> Questions { get; set; } = [];
    public string? Notes { get; set; }
    public string? Origin { get; set; }
    public bool IsCompleted => IsVerified || Questions.All(q => q.IsCompleted);
    public bool IsVerified { get; set; }

    public void AnswerQuestion(string questionId, string answer)
    {
        var q = Questions.FirstOrDefault(x => x.Id == questionId);
        if (q != null) { q.Data = q.Data with { Answer = answer }; Touch(); }
    }
}
