# Corpus management

The latest resume record in MongoDB is the source for assistant knowledge. On API startup, the backend builds searchable chunks from its summary, skills, experience, projects, education, and certifications.

Update the resume record through the established data-loading process. The API refreshes the chunks when the content hash changes.
