using ReidFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ReidFeature.Services
{
    sealed class MockPersonGroupProvider : IPersonGroupProvider
    {
        private readonly Dictionary<string, Person[]> _groups;

        public MockPersonGroupProvider(DetectService detectService)
        {
            var groups = new Dictionary<string, Person[]>();
            foreach (var groupDir in Directory.GetDirectories("datas/persongroups"))
            {
                var groupId = Path.GetFileName(groupDir);
                var personList = new List<Person>();

                foreach (var personDir in Directory.GetDirectories(groupDir))
                {
                    var personName = Path.GetFileName(personDir);
                    foreach (var imageFile in Directory.GetFiles(personDir, "*.*"))
                    {
                        using var image = Image.Load<Rgb24>(imageFile);
                        var personId = Path.GetFileName(imageFile);
                        foreach (var detection in detectService.DetectPersons(image, DetectionFlags.All))
                        {
                            var person = new Person(personId, groupId, personName, detection.Face?.Features, detection.Features);
                            personList.Add(person);
                            break;
                        }
                    }
                }

                groups[groupId] = personList.ToArray();
            }

            _groups = groups;
        }


        public Task<Person[]> GetPersonsAsync(string groupId, CancellationToken cancellationToken)
        {
            return this._groups.TryGetValue(groupId, out var persons)
                ? Task.FromResult(persons)
                : Task.FromResult(Array.Empty<Person>());
        }
    }
}
