import { buildVerifyClaimFormData } from './validator-api.service';

describe('buildVerifyClaimFormData', () => {
  it('appends Video and Metadata parts with exact names', () => {
    const video = new File(['video'], 'sample.avi', { type: 'video/avi' });
    const metadata = new File(['{}'], 'metadata.json', {
      type: 'application/json'
    });

    const formData = buildVerifyClaimFormData(video, metadata);

    expect(formData.get('Video')).toBe(video);
    expect(formData.get('Metadata')).toBe(metadata);
  });
});
