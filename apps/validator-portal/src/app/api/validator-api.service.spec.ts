import { buildVerifyClaimFormData } from './validator-api.service';

describe('buildVerifyClaimFormData', () => {
  it('appends video, sessionId, and metadata parts with exact names', () => {
    const video = new File(['video'], 'sample.avi', { type: 'video/avi' });
    const metadata = new File(['{}'], 'metadata.json', {
      type: 'application/json'
    });
    const sessionId = 'session-123';

    const formData = buildVerifyClaimFormData(video, sessionId, metadata);

    expect(formData.get('video')).toBe(video);
    expect(formData.get('sessionId')).toBe(sessionId);
    expect(formData.get('metadata')).toBe(metadata);
  });

  it('appends video and sessionId without metadata when omitted', () => {
    const video = new File(['video'], 'sample.avi', { type: 'video/avi' });
    const sessionId = 'session-456';

    const formData = buildVerifyClaimFormData(video, sessionId);

    expect(formData.get('video')).toBe(video);
    expect(formData.get('sessionId')).toBe(sessionId);
    expect(formData.get('metadata')).toBeNull();
  });
});
