using FaceFeature.Payloads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceFeature.Services
{
    sealed class MockFaceGroupProvider : IFaceGroupProvider
    {
        private readonly Dictionary<string, FacePerson[]> _groups;

        public MockFaceGroupProvider(DetectService detectService)
        {
            var groups = new Dictionary<string, FacePerson[]>();
            var faceGroupsDir = Path.Combine(AppContext.BaseDirectory, "datas", "facegroups");

            if (!Directory.Exists(faceGroupsDir))
            {
                _groups = groups;
                return;
            }

            foreach (var groupDir in Directory.GetDirectories(faceGroupsDir))
            {
                var groupId = Path.GetFileName(groupDir);
                var personList = new List<FacePerson>();

                foreach (var personDir in Directory.GetDirectories(groupDir))
                {
                    var personName = Path.GetFileName(personDir);
                    foreach (var imageFile in Directory.GetFiles(personDir, "*.*"))
                    {
                        using var image = Image.Load<Rgb24>(imageFile);
                        var personId = Path.GetFileName(imageFile);

                        foreach (var face in detectService.DetectFaces(image, DetectionFlags.All))
                        {
                            var person = new FacePerson(personId, groupId, personName, face.Features);
                            personList.Add(person);
                            break;
                        }
                    }
                }

                groups[groupId] = personList.ToArray();
            }

            _groups = groups;
        }

        public Task<FacePerson[]> GetPersonsAsync(string groupId, CancellationToken cancellationToken)
        {
            return _groups.TryGetValue(groupId, out var persons)
                ? Task.FromResult(persons)
                : Task.FromResult(Array.Empty<FacePerson>());
        }
    }
}
