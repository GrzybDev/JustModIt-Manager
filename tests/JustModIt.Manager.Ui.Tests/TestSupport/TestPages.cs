using System.Text.Json;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>Invented project pages as the site sends them, for the agent to answer a game page's question with.</summary>
public static class TestPages
{
    /// <summary>A full page of a project: description, notes, changes, gallery, team and people.</summary>
    public static ProjectPageResult Page(ProjectOfferView project) => new()
    {
        Page = JsonDocument.Parse(Json(project)).RootElement.Clone(),
        FetchedAt = TestStates.Now,
    };

    /// <summary>Answers the window's project page questions with <see cref="Page"/>, and every other request with nothing.</summary>
    public static Func<Message, Message?> Answer(ProjectOfferView project) =>
        request => request is GetProjectPage ? Page(project) : null;

    private static string Json(ProjectOfferView project) => $$"""
        {
          "uuid": "{{project.ProjectId}}",
          "slug": "{{project.ProjectSlug}}",
          "name": "{{project.ProjectName}}",
          "game": {
            "uuid": "4f0b7a2e-3c1d-4e5f-8a9b-0c1d2e3f4a5b",
            "slug": "{{project.GameSlug}}",
            "name": "{{project.GameName}}",
            "cover": "{{project.Cover}}",
            "cover_meta": { "color": "#3a5878" },
            "genres": [
              { "uuid": "11111111-2222-4333-8444-555555555555", "name": "Akcja" },
              { "uuid": "21111111-2222-4333-8444-555555555555", "name": "Bogata fabuła" }
            ]
          },
          "team": {
            "uuid": "{{project.Team.Id}}",
            "slug": "{{project.Team.Slug}}",
            "name": "{{project.Team.Name}}",
            "member_count": 3,
            "project_count": 12,
            "donation_methods": [
              { "uuid": "31111111-2222-4333-8444-555555555555", "platform": { "uuid": "41111111-2222-4333-8444-555555555555", "name": "Tip Jar", "is_link": true }, "value": "https://tips.example.com/lantern-team" }
            ]
          },
          "status": { "uuid": "51111111-2222-4333-8444-555555555555", "name": "{{project.Status?.Name}}", "slug": "completed", "color": "{{project.Status?.Color}}", "description": "Tłumaczenie jest gotowe i sprawdzone. Grupa może jeszcze wydawać poprawki.", "is_completed": true },
          "summary": "Latarnik wraca na wyspę, na której czas płynie wstecz. Każda noc odsłania inny fragment historii.",
          "description": "![](https://images.example.com/attachments/welcome.webp)\\\n**Witaj na wyspie**\\\nWyspa **Ember Hollow** skrywa tajemnicę, która zmienia się z każdą nocą.\n\n<div><video autoplay loop muted playsinline poster=\"https://images.example.com/attachments/lantern.avif\"><source src=\"https://images.example.com/attachments/lantern.mp4\" type=\"video/mp4\"><source src=\"https://images.example.com/attachments/lantern.webm\" type=\"video/webm\"></video></div>\n\nGra łączy eksplorację z zagadkami:\n\n- latarnia, która przewija czas,\n- dziennik pełen wskazówek,\n- trzy zakończenia.\n\nWięcej na [stronie twórców](https://example.com/ember-hollow).\n\n<div data-youtube-video=\"\"><iframe src=\"https://www.youtube-nocookie.com/embed/W4lkThr0ugh?start=30\"></iframe></div>",
          "installation_instructions": "- Język napisów w grze zostaw angielski.\n- Napisy włączysz w grze: **Opcje → Dźwięk → Napisy**.",
          "page_background_image": "https://images.example.com/backgrounds/{{project.GameSlug}}.png",
          "background_image": "https://images.example.com/banners/{{project.GameSlug}}.png",
          "releases": [
            {
              "uuid": "{{project.Release?.Id}}",
              "version": {{project.Release?.Version ?? 1}},
              "name": "v{{project.Release?.Version ?? 1}}",
              "changelog": "- Napisy w przerywnikach nie znikają już przed końcem kwestii.\n- Poprawione literówki zgłoszone przez graczy.",
              "is_public": true,
              "first_published_at": "2026-09-28T10:00:00Z"
            }
          ],
          "gallery": [
            { "uuid": "61111111-2222-4333-8444-555555555555", "kind": "youtube", "youtube_id": "Tr4iLer-0_1", "image": "", "caption": "Zwiastun", "position": 0 },
            { "uuid": "71111111-2222-4333-8444-555555555555", "kind": "image", "image": "https://images.example.com/gallery/one.png", "caption": "Latarnia", "position": 1 },
            { "uuid": "81111111-2222-4333-8444-555555555555", "kind": "image", "image": "https://images.example.com/gallery/two.png", "caption": "Przystań", "position": 2 }
          ],
          "credits": [
            { "uuid": "91111111-2222-4333-8444-555555555555", "name": "Ada Lantern", "role_label": "Tłumaczenie", "position": 0 },
            { "uuid": "a1111111-2222-4333-8444-555555555555", "name": "Olek Tide", "role_label": "Korekta i testy", "position": 1 }
          ]
        }
        """;
}
