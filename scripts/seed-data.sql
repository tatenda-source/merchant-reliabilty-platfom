-- MRP Seed Data: Demo merchants for Paynow Zimbabwe
-- This runs on first postgres container initialization

CREATE SCHEMA IF NOT EXISTS mrp;

-- Demo Merchants
INSERT INTO mrp.merchants ("Id", "Name", "TradingName", "ContactEmail", "ContactPhone", "Tier", "IsActive", "OnboardedAt", "ReliabilityScore")
VALUES
    ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'Harare Fresh Market', 'FreshMart', 'admin@freshmart.co.zw', '+263771234567', 'Standard', true, NOW() - INTERVAL '90 days', 92.5),
    ('b2c3d4e5-f6a7-8901-bcde-f12345678901', 'TechZim Solutions', 'TechZim', 'payments@techzim.co.zw', '+263772345678', 'Professional', true, NOW() - INTERVAL '60 days', 78.0),
    ('c3d4e5f6-a7b8-9012-cdef-123456789012', 'Bulawayo Transport Co', 'BulTrans', 'finance@bultrans.co.zw', '+263773456789', 'Enterprise', true, NOW() - INTERVAL '30 days', 55.3),
    ('d4e5f6a7-b8c9-0123-defa-234567890123', 'Mutare Online Store', 'MutareShop', 'support@mutareshop.co.zw', '+263774567890', 'Standard', false, NOW() - INTERVAL '7 days', 50.0)
ON CONFLICT DO NOTHING;

-- Demo Integrations
INSERT INTO mrp.merchant_integrations ("Id", "MerchantId", "PaynowIntegrationId", "PaynowIntegrationKey", "ResultUrl", "ReturnUrl", "IsCallbackReachable", "ConsecutiveFailures")
VALUES
    ('11111111-1111-1111-1111-111111111111', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'DEMO_ID_001', 'demo_key_001', 'https://freshmart.co.zw/paynow/result', 'https://freshmart.co.zw/payment/complete', true, 0),
    ('22222222-2222-2222-2222-222222222222', 'b2c3d4e5-f6a7-8901-bcde-f12345678901', 'DEMO_ID_002', 'demo_key_002', 'https://techzim.co.zw/paynow/result', 'https://techzim.co.zw/payment/complete', true, 0),
    ('33333333-3333-3333-3333-333333333333', 'c3d4e5f6-a7b8-9012-cdef-123456789012', 'DEMO_ID_003', 'demo_key_003', 'https://bultrans.co.zw/paynow/result', 'https://bultrans.co.zw/payment/complete', false, 3),
    ('44444444-4444-4444-4444-444444444444', 'd4e5f6a7-b8c9-0123-defa-234567890123', 'DEMO_ID_004', 'demo_key_004', 'http://mutareshop.co.zw/callback', 'http://mutareshop.co.zw/done', false, 5)
ON CONFLICT DO NOTHING;
