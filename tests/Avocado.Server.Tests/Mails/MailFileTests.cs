using System.Text;
using Avocado.Server.Features.Mails.Infrastructure;
using MimeKit;

namespace Avocado.Server.Tests.Mails;

public class MailFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"avocado-mail-{Guid.NewGuid():N}");

    public MailFileTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReadsASimpleMessage()
    {
        var mail = MailFile.Read(Write(Message("Conclusions à relire")));

        Assert.Equal("Conclusions à relire", mail.Subject);
        Assert.Equal("client@exemple.fr", mail.From?.Address);
        Assert.Equal("avocat@cabinet.fr", Assert.Single(mail.To).Address);
        Assert.Contains("Bonjour Maître", mail.BodyText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Matching must never turn on how a client's mail server capitalised the address, so everything
    /// is lower-cased on the way in rather than at each comparison.
    /// </summary>
    [Fact]
    public void LowerCasesAddressesOnTheWayIn()
    {
        var message = Message("Objet");
        message.From.Clear();
        message.From.Add(new MailboxAddress("SARL Dupont", "Client@Exemple.FR"));

        Assert.Equal("client@exemple.fr", MailFile.Read(Write(message)).From?.Address);
    }

    /// <summary>The pièce jointe is the point: a client's PDF has to become a document in the dossier.</summary>
    [Fact]
    public void PullsOutAttachments()
    {
        var message = Message("Avec pièce");
        var body = new Multipart("mixed") { new TextPart("plain") { Text = "Ci-joint." } };

        body.Add(new MimePart("application", "pdf")
        {
            Content = new MimeContent(new MemoryStream("%PDF-1.4 assignation"u8.ToArray())),
            FileName = "assignation.pdf",
        });

        message.Body = body;

        var attachment = Assert.Single(MailFile.Read(Write(message)).Attachments);

        Assert.Equal("assignation.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.StartsWith("%PDF", Encoding.UTF8.GetString(attachment.Content), StringComparison.Ordinal);
    }

    /// <summary>
    /// A file name out of a message is written by whoever sent it. Anything shaped like a path is
    /// reduced to its last segment, so a helpful « ../../vault.json » stays an ordinary name.
    /// </summary>
    [Theory]
    [InlineData("../../vault.json", "vault.json")]
    [InlineData(@"C:\Windows\System32\evil.dll", "evil.dll")]
    [InlineData("", "piece-jointe")]
    public void RefusesToLetAnAttachmentNameEscape(string sent, string expected)
    {
        var message = Message("Nom douteux");
        var body = new Multipart("mixed") { new TextPart("plain") { Text = "." } };

        body.Add(new MimePart("application", "octet-stream")
        {
            Content = new MimeContent(new MemoryStream([1, 2, 3])),
            FileName = sent,
        });

        message.Body = body;

        Assert.Equal(expected, Assert.Single(MailFile.Read(Write(message)).Attachments).FileName);
    }

    /// <summary>An HTML-only message still has to be searchable and readable in the journal.</summary>
    [Fact]
    public void FallsBackToStrippedHtmlWhenThereIsNoTextPart()
    {
        var message = Message("En HTML");
        message.Body = new TextPart("html") { Text = "<p>Bonjour <b>Ma&icirc;tre</b>,</p>" };

        var mail = MailFile.Read(Write(message));

        Assert.Contains("Bonjour", mail.BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", mail.BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public void RecognisesTheFormatsItCanRead()
    {
        Assert.True(MailFile.LooksLikeMail("message.eml"));
        Assert.True(MailFile.LooksLikeMail("MESSAGE.MSG"));
        Assert.False(MailFile.LooksLikeMail("conclusions.docx"));
    }

    /// <summary>
    /// The watcher has to keep going, so anything unreadable comes back as one known exception rather
    /// than whatever the underlying reader happened to throw.
    /// </summary>
    [Fact]
    public void ReportsAnUnreadableFileAsOneKnownFailure()
    {
        var path = Path.Combine(_directory, "cassé.eml");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02]);

        var failure = Record.Exception(() => MailFile.Read(path));

        Assert.IsType<MailFormatException>(failure);
        Assert.Contains("cassé.eml", failure.Message, StringComparison.Ordinal);
    }

    private static MimeMessage Message(string subject)
    {
        var message = new MimeMessage
        {
            Subject = subject,
            Date = new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.FromHours(2)),
            Body = new TextPart("plain") { Text = "Bonjour Maître,\n\nCi-joint mes remarques." },
        };

        message.From.Add(new MailboxAddress("SARL Dupont", "client@exemple.fr"));
        message.To.Add(new MailboxAddress("Maître", "avocat@cabinet.fr"));

        return message;
    }

    private string Write(MimeMessage message)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.eml");
        message.WriteTo(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
